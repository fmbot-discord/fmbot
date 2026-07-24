using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Bot.Resources;
using FMBot.Domain.Enums;
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

    private const int RefreshBatchSize = 10000;
    private const int SendCap = 5000;
    private const long SendAdvisoryLockKey = 20260721;

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
            var activeCutoff = now.AddMonths(-6);
            var staleCutoff = now.AddDays(-30);

            await using var db = await contextFactory.CreateDbContextAsync();

            var candidates = await db.Users
                .AsQueryable()
                .Where(w => w.LastUsed >= activeCutoff &&
                            (w.SpotifyExpiryChecked == null ||
                             (w.SpotifyExpiryChecked < staleCutoff &&
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
            foreach (var candidate in candidates)
            {
                var userInfo = await lastfmRepository.GetLfmUserInfoAsync(candidate.UserNameLastFM);

                if (userInfo != null)
                {
                    var expiry = userInfo.SpotifyExpiryEstimateUnix.HasValue
                        ? DateTime.UnixEpoch.AddSeconds(userInfo.SpotifyExpiryEstimateUnix.Value)
                        : (DateTime?)null;

                    await db.Users
                        .Where(w => w.UserId == candidate.UserId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.SpotifyConnectionExpiry, expiry)
                            .SetProperty(p => p.SpotifyExpiryChecked, DateTime.UtcNow));
                    updated++;
                }
                else
                {
                    await db.Users
                        .Where(w => w.UserId == candidate.UserId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.SpotifyExpiryChecked, DateTime.UtcNow));
                    failed++;
                }

                await Task.Delay(600);
            }

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
                var activeCutoff = now.AddMonths(-6);
                var windowEnd = now.AddDays(3);
                var notifiedCutoff = now.AddDays(-150);

                var candidates = await db.Users
                    .AsQueryable()
                    .Where(w => w.LastUsed >= activeCutoff &&
                                w.Blocked != true &&
                                w.SpotifyConnectionExpiry != null &&
                                w.SpotifyConnectionExpiry <= windowEnd &&
                                !db.UserDmNotifications.Any(n => n.DiscordUserId == w.DiscordUserId &&
                                                                 n.Type == UserDmNotificationType.SpotifyExpiryWarning &&
                                                                 n.Sent >= notifiedCutoff))
                    .OrderByDescending(o => o.SpotifyConnectionExpiry)
                    .Take(sendCap * 2)
                    .Select(s => new { s.UserId, s.DiscordUserId, s.UserNameLastFM, s.SpotifyConnectionExpiry, s.DmChannelId })
                    .ToListAsync();

                candidates = candidates
                    .DistinctBy(d => d.DiscordUserId)
                    .ToList();

                if (candidates.Count == 0)
                {
                    return (0, 0, 0);
                }

                var candidateDiscordUserIds = candidates.Select(s => s.DiscordUserId).ToList();
                var previousNotifications = await db.UserDmNotifications
                    .Where(w => w.Type == UserDmNotificationType.SpotifyExpiryWarning &&
                                candidateDiscordUserIds.Contains(w.DiscordUserId))
                    .Select(s => new { s.DiscordUserId, s.Reference })
                    .ToListAsync();

                var notifiedExpiries = previousNotifications
                    .GroupBy(g => g.DiscordUserId)
                    .ToDictionary(d => d.Key, d => d
                        .Where(w => long.TryParse(w.Reference, out _))
                        .Select(s => long.Parse(s.Reference))
                        .ToList());

                Log.Information("DmNotificationService: Found {count} Spotify expiry notification candidates", candidates.Count);

                var sent = 0;
                var skipped = 0;
                var failedSends = 0;
                foreach (var candidate in candidates)
                {
                    if (sent + failedSends >= sendCap)
                    {
                        Log.Warning("DmNotificationService: Send cap of {cap} reached, {remaining} candidates deferred to next run",
                            sendCap, candidates.Count - sent - failedSends - skipped);
                        break;
                    }

                    var storedExpiryUnix = ((DateTimeOffset)DateTime.SpecifyKind(candidate.SpotifyConnectionExpiry.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                    if (notifiedExpiries.TryGetValue(candidate.DiscordUserId, out var previousExpiries) &&
                        previousExpiries.Any(a => Math.Abs(a - storedExpiryUnix) < (long)TimeSpan.FromDays(7).TotalSeconds))
                    {
                        skipped++;
                        continue;
                    }

                    var userInfo = await lastfmRepository.GetLfmUserInfoAsync(candidate.UserNameLastFM);
                    if (userInfo == null)
                    {
                        skipped++;
                        continue;
                    }

                    var freshExpiry = userInfo.SpotifyExpiryEstimateUnix.HasValue
                        ? DateTime.UnixEpoch.AddSeconds(userInfo.SpotifyExpiryEstimateUnix.Value)
                        : (DateTime?)null;

                    await db.Users
                        .Where(w => w.UserId == candidate.UserId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.SpotifyConnectionExpiry, freshExpiry)
                            .SetProperty(p => p.SpotifyExpiryChecked, DateTime.UtcNow));

                    if (freshExpiry == null || freshExpiry > DateTime.UtcNow.AddDays(3))
                    {
                        skipped++;
                        continue;
                    }

                    var expired = freshExpiry < DateTime.UtcNow;

                    var notification = new UserDmNotification
                    {
                        UserId = candidate.UserId,
                        DiscordUserId = candidate.DiscordUserId,
                        Type = UserDmNotificationType.SpotifyExpiryWarning,
                        Sent = DateTime.UtcNow,
                        Reference = userInfo.SpotifyExpiryEstimateUnix.Value.ToString(),
                        Successful = false
                    };
                    db.UserDmNotifications.Add(notification);
                    await db.SaveChangesAsync();

                    var (successful, dmChannelId) = await SendDm(candidate.UserId, candidate.DiscordUserId, candidate.DmChannelId, BuildSpotifyExpiryMessage(expired));

                    if (dmChannelId != null && dmChannelId != candidate.DmChannelId)
                    {
                        await db.Users
                            .Where(w => w.UserId == candidate.UserId)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(p => p.DmChannelId, dmChannelId));
                    }

                    if (successful)
                    {
                        notification.Successful = true;
                        await db.SaveChangesAsync();
                        sent++;
                    }
                    else
                    {
                        failedSends++;
                    }

                    await Task.Delay(1000);
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
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found in database.";
                }

                var now = DateTime.UtcNow;

                var previousNotifications = await db.UserDmNotifications
                    .Where(w => w.DiscordUserId == discordUserId &&
                                w.Type == UserDmNotificationType.SpotifyExpiryWarning)
                    .Select(s => new { s.Sent, s.Reference })
                    .ToListAsync();

                if (!bypassChecks)
                {
                    var notifiedCutoff = now.AddDays(-150);
                    if (previousNotifications.Any(a => a.Sent >= notifiedCutoff))
                    {
                        return "Skipped: user already has a Spotify expiry notification within the last 150 days. Add `force` to send anyway.";
                    }
                }

                var userInfo = await lastfmRepository.GetLfmUserInfoAsync(user.UserNameLastFM);
                if (userInfo == null)
                {
                    return $"Skipped: could not fetch Last.fm info for `{user.UserNameLastFM}`.";
                }

                var freshExpiry = userInfo.SpotifyExpiryEstimateUnix.HasValue
                    ? DateTime.UnixEpoch.AddSeconds(userInfo.SpotifyExpiryEstimateUnix.Value)
                    : (DateTime?)null;

                await db.Users
                    .Where(w => w.UserId == user.UserId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.SpotifyConnectionExpiry, freshExpiry)
                        .SetProperty(p => p.SpotifyExpiryChecked, now));

                if (freshExpiry == null)
                {
                    return $"Skipped: `{user.UserNameLastFM}` has no Spotify expiry estimate on Last.fm.";
                }

                var expiryUnix = userInfo.SpotifyExpiryEstimateUnix.Value;

                if (!bypassChecks)
                {
                    if (freshExpiry > now.AddDays(3))
                    {
                        return $"Skipped: Spotify expiry <t:{expiryUnix}:D> is not within the 3-day warning window. Add `force` to send anyway.";
                    }

                    if (previousNotifications.Any(a => long.TryParse(a.Reference, out var reference) &&
                                                       Math.Abs(reference - expiryUnix) < (long)TimeSpan.FromDays(7).TotalSeconds))
                    {
                        return "Skipped: user was already notified for this expiry date. Add `force` to send anyway.";
                    }
                }

                var expired = freshExpiry < now;

                var notification = new UserDmNotification
                {
                    UserId = user.UserId,
                    DiscordUserId = discordUserId,
                    Type = UserDmNotificationType.SpotifyExpiryWarning,
                    Sent = DateTime.UtcNow,
                    Reference = expiryUnix.ToString(),
                    Successful = false
                };
                db.UserDmNotifications.Add(notification);
                await db.SaveChangesAsync();

                var (successful, dmChannelId) = await SendDm(user.UserId, discordUserId, user.DmChannelId, BuildSpotifyExpiryMessage(expired));

                if (dmChannelId != null && dmChannelId != user.DmChannelId)
                {
                    await db.Users
                        .Where(w => w.UserId == user.UserId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.DmChannelId, dmChannelId));
                }

                if (successful)
                {
                    notification.Successful = true;
                    await db.SaveChangesAsync();
                    return $"✅ Sent Spotify expiry DM to `{user.UserNameLastFM}` with the {(expired ? "'expired'" : "'expiring soon'")} variant. Expiry: <t:{expiryUnix}:D>.";
                }

                return "❌ Could not DM this user. Logged as unsuccessful, they will not be retried automatically.";
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

    private static MessageProperties BuildSpotifyExpiryMessage(bool expired)
    {
        var container = new ComponentContainerProperties
        {
            AccentColor = DiscordConstants.SpotifyColorGreen
        };

        var notification = new StringBuilder();
        notification.AppendLine(expired
            ? "Your Last.fm connection with Spotify has expired. Please reconnect Spotify to your Last.fm to ensure that your Spotify will continue to be tracked on Last.fm and there will be no gaps on your listening history."
            : "Your Last.fm connection with Spotify is expiring in 3 days. Please reconnect Spotify to your Last.fm to ensure that your Spotify will continue to be tracked on Last.fm and there will be no gaps on your listening history.");
        notification.AppendLine();
        notification.Append("**[Click here to access your Last.fm application settings.](https://www.last.fm/settings/applications)** To reconnect, press the reconnect button below 'Spotify Scrobbling'.");
        container.AddComponent(new TextDisplayProperties(notification.ToString()));

        container.AddComponent(new ActionRowProperties()
            .AddComponents(new LinkButtonProperties("https://www.last.fm/settings/applications", "Last.fm application settings")));

        container.AddComponent(new ComponentSeparatorProperties());

        var disclosure = new StringBuilder();
        disclosure.AppendLine(expired
            ? "You are receiving this message because you have recently used .fmbot and we have detected your Spotify connection has expired. This is a one-time message that will not be sent again, unless we detect that your Spotify connection is close to expiring again in the future. Keep in mind that .fmbot is not affiliated with Last.fm."
            : "You are receiving this message because you have recently used .fmbot and we have detected your Spotify connection is expiring soon. This is a one-time message that will not be sent again, unless we detect that your Spotify connection is close to expiring again in the future. Keep in mind that .fmbot is not affiliated with Last.fm.");
        disclosure.AppendLine();
        disclosure.Append("Spotify has recently made changes causing connected applications to have to re-authenticate every six months. You can read more [about this here](https://developer.spotify.com/blog/2026-06-18-refresh-token-expiration) or on [the Last.fm forums](https://support.last.fm/t/important-change-to-spotify-scrobbling-spotifys-new-refresh-token-policy/119488).");
        container.AddComponent(new TextDisplayProperties(disclosure.ToString()));

        return new MessageProperties
        {
            Components = [container],
            Flags = MessageFlags.IsComponentsV2,
            AllowedMentions = AllowedMentionsProperties.None
        };
    }
}
