using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Bot.Resources;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Interfaces;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Serilog;

namespace FMBot.Bot.Services;

public class DmNotificationService(
    IDbContextFactory<FMBotDbContext> contextFactory,
    ILastfmRepository lastfmRepository,
    ShardedGatewayClient client)
{
    private static int _refreshRunning;
    private static int _sendRunning;

    private const int RefreshBatchSize = 20000;
    private const int RefreshConcurrency = 4;
    private const int SendCap = 20000;
    private const int PrefetchBufferSize = 8;
    private const long SendAdvisoryLockKey = 20260721;

    private const int ActiveUserMonths = 6;
    private const int ExpiryRecheckDays = 150;
    private const int NotificationCooldownDays = 150;
    private static readonly TimeSpan InitialWarningWindow = TimeSpan.FromDays(10);
    private static readonly TimeSpan FinalWarningWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan HandledExpiryMargin = TimeSpan.FromDays(3);

    public enum SpotifyExpiryDmOutcome
    {
        Sent,
        SendFailed,
        NoLastfmInfo,
        NoExpiryEstimate,
        OutsideWarningWindow,
        ExpiryAlreadyHandled,
        InCooldown
    }

    public sealed record SpotifyExpiryDmResult(
        SpotifyExpiryDmOutcome Outcome,
        UserDmNotificationType Stage,
        long? ExpiryUnix,
        bool Expired);

    internal sealed record SpotifyExpiryCandidate(
        int UserId,
        ulong DiscordUserId,
        string UserNameLastFM,
        ulong? DmChannelId);

    private sealed record PreviousNotification(
        UserDmNotificationType Type,
        DateTime Sent);

    public async Task RefreshSpotifyExpiryEstimates()
    {
        if (Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0)
        {
            Log.Information("DmNotificationService: Spotify expiry refresh already running, skipping");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var activeCutoff = now.AddMonths(-ActiveUserMonths);
            var recheckCutoff = now.AddDays(-ExpiryRecheckDays);

            await using var db = await contextFactory.CreateDbContextAsync();

            var candidates = await db.Users
                .AsQueryable()
                .Where(w => w.LastUsed >= activeCutoff &&
                            (w.SpotifyExpiryChecked == null ||
                             (w.SpotifyExpiryChecked < recheckCutoff &&
                              (w.SpotifyConnectionExpiry == null || w.SpotifyConnectionExpiry < now))))
                .OrderByDescending(o => o.LastUsed)
                .Take(RefreshBatchSize)
                .Select(s => new { s.UserId, s.UserNameLastFM })
                .ToListAsync();

            if (candidates.Count == 0)
            {
                return;
            }

            Log.Information("DmNotificationService: Refreshing Spotify expiry estimates for {count} users", candidates.Count);

            var updated = 0;
            var failed = 0;
            await Parallel.ForEachAsync(candidates,
                new ParallelOptions { MaxDegreeOfParallelism = RefreshConcurrency },
                async (candidate, cancellationToken) =>
                {
                    var userInfo = await lastfmRepository.GetLfmUserInfoAsync(candidate.UserNameLastFM);

                    await using var workerDb = await contextFactory.CreateDbContextAsync(cancellationToken);
                    if (userInfo != null)
                    {
                        await StoreExpiryEstimate(workerDb, candidate.UserId, ToExpiryDate(userInfo), cancellationToken);
                        Interlocked.Increment(ref updated);
                    }
                    else
                    {
                        await workerDb.Users
                            .Where(w => w.UserId == candidate.UserId)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(p => p.SpotifyExpiryChecked, DateTime.UtcNow), cancellationToken);
                        Interlocked.Increment(ref failed);
                    }

                    await Task.Delay(600, cancellationToken);
                });

            Log.Information("DmNotificationService: Refreshed Spotify expiry estimates - {updated} updated, {failed} failed", updated, failed);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
        }
    }

    public async Task SendSpotifyExpiryNotifications()
    {
        await SendSpotifyExpiryNotifications(SendCap);
    }

    public async Task<(int sent, int failedSends, int skipped)?> SendSpotifyExpiryNotifications(int sendCap)
    {
        if (Interlocked.CompareExchange(ref _sendRunning, 1, 0) != 0)
        {
            Log.Information("DmNotificationService: Spotify expiry notifications already running, skipping");
            return null;
        }

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync();

            if (!await TryAcquireSendLock(db))
            {
                Log.Information("DmNotificationService: Spotify expiry send lock held by another process, skipping");
                return null;
            }

            try
            {
                var now = DateTime.UtcNow;

                var finalStage = await GetCandidates(db, UserDmNotificationType.SpotifyExpiryFinalWarning, now, sendCap);
                var initialStage = await GetCandidates(db, UserDmNotificationType.SpotifyExpiryWarning, now, sendCap);

                var candidates = finalStage
                    .Concat(initialStage)
                    .DistinctBy(d => d.DiscordUserId)
                    .ToList();

                if (candidates.Count == 0)
                {
                    return (0, 0, 0);
                }

                Log.Information("DmNotificationService: Found {count} Spotify expiry notification candidates ({finalCount} final, {initialCount} initial)",
                    candidates.Count, finalStage.Count, initialStage.Count);

                var previousNotifications = await GetPreviousNotifications(db, candidates.Select(s => s.DiscordUserId).ToList());

                var sent = 0;
                var failedSends = 0;
                var skipped = 0;

                await foreach (var (candidate, userInfo) in WithLastfmUserInfo(candidates))
                {
                    if (sent + failedSends >= sendCap)
                    {
                        Log.Warning("DmNotificationService: Send cap of {cap} reached, {remaining} candidates deferred to next run",
                            sendCap, candidates.Count - sent - failedSends - skipped);
                        break;
                    }

                    var previous = previousNotifications.GetValueOrDefault(candidate.DiscordUserId) ?? [];
                    var result = await SendSpotifyExpiryDm(db, candidate, userInfo, previous, bypassChecks: false);

                    switch (result.Outcome)
                    {
                        case SpotifyExpiryDmOutcome.Sent:
                            sent++;
                            await Task.Delay(400);
                            break;
                        case SpotifyExpiryDmOutcome.SendFailed:
                            failedSends++;
                            await Task.Delay(400);
                            break;
                        default:
                            skipped++;
                            break;
                    }
                }

                Log.Information("DmNotificationService: Spotify expiry notifications done - {sent} sent, {failedSends} failed, {skipped} skipped", sent, failedSends, skipped);

                return (sent, failedSends, skipped);
            }
            finally
            {
                await ReleaseSendLock(db);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sendRunning, 0);
        }
    }

    public async Task<string> SendSpotifyExpiryNotificationToUser(ulong discordUserId, bool bypassChecks = false)
    {
        if (Interlocked.CompareExchange(ref _sendRunning, 1, 0) != 0)
        {
            return "A Spotify expiry send is already running in this process, try again later.";
        }

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync();

            if (!await TryAcquireSendLock(db))
            {
                return "A Spotify expiry send is already running in another process, try again later.";
            }

            try
            {
                var user = await db.Users
                    .Where(w => w.DiscordUserId == discordUserId)
                    .OrderByDescending(o => o.LastUsed)
                    .Select(s => new SpotifyExpiryCandidate(s.UserId, s.DiscordUserId, s.UserNameLastFM, s.DmChannelId))
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found in database.";
                }

                var previous = (await GetPreviousNotifications(db, [discordUserId])).GetValueOrDefault(discordUserId) ?? [];
                var userInfo = await lastfmRepository.GetLfmUserInfoAsync(user.UserNameLastFM);

                var result = await SendSpotifyExpiryDm(db, user, userInfo, previous, bypassChecks);

                return result.Outcome switch
                {
                    SpotifyExpiryDmOutcome.NoLastfmInfo =>
                        $"Skipped: could not fetch Last.fm info for `{user.UserNameLastFM}`.",
                    SpotifyExpiryDmOutcome.NoExpiryEstimate =>
                        $"Skipped: `{user.UserNameLastFM}` has no Spotify expiry estimate on Last.fm.",
                    SpotifyExpiryDmOutcome.OutsideWarningWindow =>
                        $"Skipped: Spotify expiry <t:{result.ExpiryUnix}:D> is not within the {InitialWarningWindow.TotalDays:0}-day warning window. Add `force` to send anyway.",
                    SpotifyExpiryDmOutcome.ExpiryAlreadyHandled =>
                        $"Skipped: user already received a DM for the expiry on <t:{result.ExpiryUnix}:D>. Add `force` to send anyway.",
                    SpotifyExpiryDmOutcome.InCooldown =>
                        $"Skipped: user already has a Spotify expiry notification within the last {NotificationCooldownDays} days. Add `force` to send anyway.",
                    SpotifyExpiryDmOutcome.Sent =>
                        $"✅ Sent Spotify expiry DM to `{user.UserNameLastFM}` with the {DescribeVariant(result)} variant. Expiry: <t:{result.ExpiryUnix}:D>.",
                    _ =>
                        "❌ Could not DM this user. Logged as unsuccessful, they will not be retried automatically."
                };
            }
            finally
            {
                await ReleaseSendLock(db);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sendRunning, 0);
        }
    }

    private static string DescribeVariant(SpotifyExpiryDmResult result)
    {
        if (result.Expired)
        {
            return "'expired'";
        }

        return result.Stage == UserDmNotificationType.SpotifyExpiryFinalWarning ? "'final reminder'" : "'expiring soon'";
    }

    internal static Task<List<SpotifyExpiryCandidate>> GetCandidates(FMBotDbContext db, UserDmNotificationType stage, DateTime now, int take)
    {
        var activeCutoff = now.AddMonths(-ActiveUserMonths);
        var cooldownCutoff = now.AddDays(-NotificationCooldownDays);
        var handledMarginDays = -HandledExpiryMargin.TotalDays;

        var isInitialStage = stage == UserDmNotificationType.SpotifyExpiryWarning;
        var finalWindowEnd = now.Add(FinalWarningWindow);
        var initialWindowEnd = now.Add(InitialWarningWindow);
        DateTime? windowStart = isInitialStage ? finalWindowEnd : null;
        var windowEnd = isInitialStage ? initialWindowEnd : finalWindowEnd;

        return db.Users
            .AsQueryable()
            .Where(u => u.LastUsed >= activeCutoff &&
                        u.Blocked != true &&
                        u.SpotifyConnectionExpiry != null &&
                        u.SpotifyConnectionExpiry <= windowEnd &&
                        (windowStart == null || u.SpotifyConnectionExpiry > windowStart) &&
                        !db.UserDmNotifications.Any(n => n.DiscordUserId == u.DiscordUserId &&
                                                         (n.Type == UserDmNotificationType.SpotifyExpiryWarning ||
                                                          n.Type == UserDmNotificationType.SpotifyExpiryFinalWarning) &&
                                                         n.Sent >= u.SpotifyConnectionExpiry.Value.AddDays(handledMarginDays)) &&
                        !db.UserDmNotifications.Any(n => n.DiscordUserId == u.DiscordUserId &&
                                                         (n.Type == UserDmNotificationType.SpotifyExpiryWarning ||
                                                          n.Type == UserDmNotificationType.SpotifyExpiryFinalWarning) &&
                                                         n.Sent >= cooldownCutoff &&
                                                         (isInitialStage || n.Type == UserDmNotificationType.SpotifyExpiryFinalWarning)))
            .OrderByDescending(o => o.SpotifyConnectionExpiry)
            .Take(take)
            .Select(s => new SpotifyExpiryCandidate(s.UserId, s.DiscordUserId, s.UserNameLastFM, s.DmChannelId))
            .ToListAsync();
    }

    private static async Task<Dictionary<ulong, List<PreviousNotification>>> GetPreviousNotifications(FMBotDbContext db, List<ulong> discordUserIds)
    {
        var rows = await db.UserDmNotifications
            .Where(w => (w.Type == UserDmNotificationType.SpotifyExpiryWarning ||
                         w.Type == UserDmNotificationType.SpotifyExpiryFinalWarning) &&
                        discordUserIds.Contains(w.DiscordUserId))
            .Select(s => new { s.DiscordUserId, s.Type, s.Sent })
            .ToListAsync();

        return rows
            .GroupBy(g => g.DiscordUserId)
            .ToDictionary(d => d.Key, d => d.Select(s => new PreviousNotification(s.Type, s.Sent)).ToList());
    }

    private static SpotifyExpiryDmOutcome? GetSkipReason(IReadOnlyCollection<PreviousNotification> previous, UserDmNotificationType stage, DateTime expiry, DateTime now)
    {
        if (previous.Any(n => n.Sent >= expiry.Subtract(HandledExpiryMargin)))
        {
            return SpotifyExpiryDmOutcome.ExpiryAlreadyHandled;
        }

        var cooldownCutoff = now.AddDays(-NotificationCooldownDays);
        var isInitialStage = stage == UserDmNotificationType.SpotifyExpiryWarning;
        if (previous.Any(n => n.Sent >= cooldownCutoff && (isInitialStage || n.Type == UserDmNotificationType.SpotifyExpiryFinalWarning)))
        {
            return SpotifyExpiryDmOutcome.InCooldown;
        }

        return null;
    }

    private static UserDmNotificationType GetWarningStage(DateTime expiry, DateTime now)
    {
        return expiry <= now.Add(FinalWarningWindow)
            ? UserDmNotificationType.SpotifyExpiryFinalWarning
            : UserDmNotificationType.SpotifyExpiryWarning;
    }

    private async Task<SpotifyExpiryDmResult> SendSpotifyExpiryDm(FMBotDbContext db, SpotifyExpiryCandidate user, DataSourceUser userInfo,
        IReadOnlyCollection<PreviousNotification> previous, bool bypassChecks)
    {
        if (userInfo == null)
        {
            return new SpotifyExpiryDmResult(SpotifyExpiryDmOutcome.NoLastfmInfo, UserDmNotificationType.SpotifyExpiryWarning, null, false);
        }

        var expiry = ToExpiryDate(userInfo);
        await StoreExpiryEstimate(db, user.UserId, expiry, CancellationToken.None);

        if (expiry == null)
        {
            return new SpotifyExpiryDmResult(SpotifyExpiryDmOutcome.NoExpiryEstimate, UserDmNotificationType.SpotifyExpiryWarning, null, false);
        }

        var now = DateTime.UtcNow;
        var expiryUnix = userInfo.SpotifyExpiryEstimateUnix.Value;
        var stage = GetWarningStage(expiry.Value, now);
        var expired = expiry < now;

        if (!bypassChecks)
        {
            if (expiry > now.Add(InitialWarningWindow))
            {
                return new SpotifyExpiryDmResult(SpotifyExpiryDmOutcome.OutsideWarningWindow, stage, expiryUnix, expired);
            }

            var skipReason = GetSkipReason(previous, stage, expiry.Value, now);
            if (skipReason != null)
            {
                return new SpotifyExpiryDmResult(skipReason.Value, stage, expiryUnix, expired);
            }
        }

        var notification = new UserDmNotification
        {
            UserId = user.UserId,
            DiscordUserId = user.DiscordUserId,
            Type = stage,
            Sent = now,
            Reference = expiryUnix.ToString(),
            Successful = false
        };
        db.UserDmNotifications.Add(notification);
        await db.SaveChangesAsync();

        var finalReminderFollows = stage == UserDmNotificationType.SpotifyExpiryWarning &&
                                   expiry.Value.Subtract(now) > HandledExpiryMargin;
        var message = BuildSpotifyExpiryMessage(stage, expiryUnix, expired, finalReminderFollows, user.UserNameLastFM);

        var (successful, dmChannelId) = await SendDm(user.UserId, user.DiscordUserId, user.DmChannelId, message);

        if (dmChannelId != null && dmChannelId != user.DmChannelId)
        {
            await db.Users
                .Where(w => w.UserId == user.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.DmChannelId, dmChannelId));
        }

        if (!successful)
        {
            return new SpotifyExpiryDmResult(SpotifyExpiryDmOutcome.SendFailed, stage, expiryUnix, expired);
        }

        notification.Successful = true;
        await db.SaveChangesAsync();
        return new SpotifyExpiryDmResult(SpotifyExpiryDmOutcome.Sent, stage, expiryUnix, expired);
    }

    private static DateTime? ToExpiryDate(DataSourceUser userInfo)
    {
        return userInfo.SpotifyExpiryEstimateUnix.HasValue
            ? DateTime.UnixEpoch.AddSeconds(userInfo.SpotifyExpiryEstimateUnix.Value)
            : null;
    }

    private static Task<int> StoreExpiryEstimate(FMBotDbContext db, int userId, DateTime? expiry, CancellationToken cancellationToken)
    {
        return db.Users
            .Where(w => w.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.SpotifyConnectionExpiry, expiry)
                .SetProperty(p => p.SpotifyExpiryChecked, DateTime.UtcNow), cancellationToken);
    }

    private async IAsyncEnumerable<(SpotifyExpiryCandidate Candidate, DataSourceUser UserInfo)> WithLastfmUserInfo(
        IReadOnlyList<SpotifyExpiryCandidate> candidates, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stopPrefetching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = System.Threading.Channels.Channel.CreateBounded<(SpotifyExpiryCandidate, DataSourceUser)>(
            new System.Threading.Channels.BoundedChannelOptions(PrefetchBufferSize) { SingleReader = true, SingleWriter = true });

        var prefetcher = Task.Run(async () =>
        {
            try
            {
                foreach (var candidate in candidates)
                {
                    stopPrefetching.Token.ThrowIfCancellationRequested();
                    var userInfo = await lastfmRepository.GetLfmUserInfoAsync(candidate.UserNameLastFM);
                    await buffer.Writer.WriteAsync((candidate, userInfo), stopPrefetching.Token);
                }
            }
            catch (OperationCanceledException) when (stopPrefetching.IsCancellationRequested)
            {
            }
            finally
            {
                buffer.Writer.Complete();
            }
        });

        try
        {
            await foreach (var item in buffer.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            stopPrefetching.Cancel();
            await prefetcher;
        }
    }

    private static async Task<bool> TryAcquireSendLock(FMBotDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT pg_try_advisory_lock({SendAdvisoryLockKey})";
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task ReleaseSendLock(FMBotDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT pg_advisory_unlock({SendAdvisoryLockKey})";
        await command.ExecuteScalarAsync();
    }

    private async Task<(bool successful, ulong? dmChannelId)> SendDm(int userId, ulong discordUserId, ulong? storedChannelId, MessageProperties message)
    {
        var channelId = storedChannelId;
        try
        {
            if (channelId == null)
            {
                var channel = await client.Rest.GetDMChannelAsync(discordUserId);
                channelId = channel.Id;
            }

            try
            {
                await client.Rest.SendMessageAsync(channelId.Value, message);
            }
            catch (RestException e) when (storedChannelId != null && e.StatusCode == HttpStatusCode.NotFound)
            {
                var channel = await client.Rest.GetDMChannelAsync(discordUserId);
                channelId = channel.Id;
                await client.Rest.SendMessageAsync(channelId.Value, message);
            }

            return (true, channelId);
        }
        catch (Exception e)
        {
            Log.Information(e, "DmNotificationService: Could not send DM to {userId} / {discordUserId}", userId, discordUserId);
            return (false, channelId);
        }
    }

    private static MessageProperties BuildSpotifyExpiryMessage(UserDmNotificationType stage, long expiryUnix, bool expired, bool finalReminderFollows, string lastfmUserName)
    {
        var finalWarning = stage == UserDmNotificationType.SpotifyExpiryFinalWarning;

        var container = new ComponentContainerProperties
        {
            AccentColor = DiscordConstants.SpotifyColorGreen
        };

        var notification = new StringBuilder();
        if (expired)
        {
            notification.AppendLine("Your Last.fm connection with Spotify has expired. Please reconnect Spotify to your Last.fm to ensure that your Spotify will continue to be tracked on Last.fm and there will be no gaps in your listening history.");
        }
        else if (finalWarning)
        {
            notification.AppendLine($"Your Last.fm connection with Spotify is expiring <t:{expiryUnix}:R>. Please reconnect Spotify to your Last.fm as soon as possible to ensure that your Spotify will continue to be tracked on Last.fm and there will be no gaps in your listening history.");
        }
        else
        {
            notification.AppendLine($"Your Last.fm connection with Spotify is expiring <t:{expiryUnix}:R> (<t:{expiryUnix}:D>). Please reconnect Spotify to your Last.fm to ensure that your Spotify will continue to be tracked on Last.fm and there will be no gaps in your listening history.");
        }
        notification.AppendLine();
        notification.Append($"**[Click here to access your Last.fm application settings.](https://www.last.fm/settings/applications)** To reconnect, use the 'Disconnect' and 'Connect' button on 'Spotify Scrobbling'. Make sure you're logged into Last.fm as `{lastfmUserName}`.");
        container.AddComponent(new TextDisplayProperties(notification.ToString()));

        container.AddComponent(new ActionRowProperties()
            .AddComponents(new LinkButtonProperties("https://www.last.fm/settings/applications", "Last.fm application settings")));

        container.AddComponent(new ComponentSeparatorProperties());

        var reason = expired
            ? "we have detected your Spotify connection has expired"
            : "we have detected your Spotify connection is expiring soon";
        var followUp = finalWarning && !expired
            ? "This is the final reminder for this expiry and will not be sent again, unless we detect that your Spotify connection is close to expiring again in the future."
            : finalReminderFollows
                ? "If you haven't reconnected by then, we will send you one final reminder the day before it expires. After that you will not hear from us again, unless we detect that your Spotify connection is close to expiring again in the future."
                : "This is a one-time message that will not be sent again, unless we detect that your Spotify connection is close to expiring again in the future.";

        var disclosure = new StringBuilder();
        disclosure.AppendLine($"You are receiving this message because you have recently used .fmbot and {reason}. {followUp} Keep in mind that .fmbot is not affiliated with Last.fm.");
        container.AddComponent(new TextDisplayProperties(disclosure.ToString()));

        return new MessageProperties
        {
            Components = [container],
            Flags = MessageFlags.IsComponentsV2,
            AllowedMentions = AllowedMentionsProperties.None
        };
    }
}
