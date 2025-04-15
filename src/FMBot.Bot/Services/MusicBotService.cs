using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Models.MusicBot;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class MusicBotService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly TrackService _trackService;
    private readonly IMemoryCache _cache;
    private readonly IPrefixService _prefixService;
    public readonly List<BotScrobblingLog> BotScrobblingLogs;

    public record BotScrobblingLog(ulong GuildId, DateTime DateTime, string Log);

    private InteractiveService Interactivity { get; }

    public MusicBotService(IDbContextFactory<FMBotDbContext> contextFactory,
        IDataSourceFactory dataSourceFactory,
        TrackService trackService,
        IMemoryCache cache,
        InteractiveService interactivity,
        IPrefixService prefixService)
    {
        this._contextFactory = contextFactory;
        this._dataSourceFactory = dataSourceFactory;
        this._trackService = trackService;
        this._cache = cache;
        this.Interactivity = interactivity;
        this._prefixService = prefixService;
        this.BotScrobblingLogs = new List<BotScrobblingLog>();
    }

    public async Task Scrobble(MusicBot musicBot, SocketUserMessage msg, ICommandContext context)
    {
        try
        {
            if (context.Guild == null || musicBot.ShouldIgnoreMessage(msg))
            {
                return;
            }
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Found 'now playing' message from {musicBot.Name}"));
            var usersInChannel = await GetUsersInVoice(context, msg.Author.Id);

            if (usersInChannel == null || usersInChannel.Count == 0)
            {
                return;
            }

            var trackDescription = musicBot.GetTrackQuery(msg);
            var trackResult = await this._trackService.GetTrackFromLink(trackDescription, musicBot.PossiblyIncludesLinks, musicBot.SkipUploaderName, musicBot.TrackNameFirst);
            if (trackResult == null)
            {
                Log.Information("BotScrobbling({botName}): Skipped scrobble for {listenerCount} users in {guildName} / {guildId} because no found track for {trackDescription}", musicBot.Name, usersInChannel.Count, context.Guild.Name, context.Guild.Id, msg.Embeds.First().Description);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped {musicBot.Name} scrobble because no found track for `{msg.Embeds.First().Description}`"));
                return;
            }

            _ = RegisterTrack(usersInChannel, trackResult, musicBot);
            _ = SendScrobbleMessage(context, trackResult, usersInChannel.Count, msg.Id);
        }
        catch (Exception e)
        {
            Log.Error($"BotScrobbling: Error in music bot scrobbler ({musicBot.Name})", e);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, "Skipped scrobble because error"));
        }
    }

    private async Task SendScrobbleMessage(ICommandContext context, TrackSearchResult trackResult,
        int listenerCount, ulong botMessageId)
    {
        var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
        var prfx = this._prefixService.GetPrefix(context.Guild?.Id);

        var description = new StringBuilder();
        description.Append($"Scrobbling **{trackResult.TrackName}** by **{trackResult.ArtistName}** for {listenerCount} {StringExtensions.GetListenersString(listenerCount)}");
        embed.WithDescription(description.ToString());

        var footer = new StringBuilder();
        if (trackResult.DurationMs.HasValue)
        {
            footer.Append($"Length {StringExtensions.GetTrackLength(trackResult.DurationMs.GetValueOrDefault())} - ");
        }
        footer.Append($"Manage with '{prfx}botscrobbling'");
        embed.WithFooter(footer.ToString());

        var scrobbleMessage =
            await context.Channel.SendMessageAsync(embed: embed.Build(), flags: MessageFlags.SuppressNotification);

        var referencedMusic = new ReferencedMusic
        {
            Artist = trackResult.ArtistName,
            Album = trackResult.AlbumName,
            Track = trackResult.TrackName
        };

        PublicProperties.UsedCommandsReferencedMusic.TryAdd(scrobbleMessage.Id, referencedMusic);
        PublicProperties.UsedCommandsReferencedMusic.TryAdd(botMessageId, referencedMusic);

        Log.Information("BotScrobbling: Scrobbled {trackName} by {artistName} for {listenerCount} users in {guildName} / {guildId}", trackResult.TrackName, trackResult.ArtistName, listenerCount, context.Guild?.Name, context.Guild?.Id);
        this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Scrobbled `{trackResult.TrackName}` by `{trackResult.ArtistName}`"));

        var messageDelayMs = (int)(trackResult.DurationMs - 5000 ?? 120000);
        await Task.Delay(messageDelayMs);

        await scrobbleMessage.DeleteAsync();
    }

    private Task RegisterTrack(IEnumerable<User> users, TrackSearchResult result, MusicBot musicBot)
    {
        foreach (var user in users)
        {
            _ = RegisterTrackForUser(user, result, musicBot);
        }

        return Task.CompletedTask;
    }

    private async Task RegisterTrackForUser(User user, TrackSearchResult result, MusicBot musicBot)
    {
        try
        {
            await this._dataSourceFactory.SetNowPlayingAsync(user.SessionKeyLastFm, result.ArtistName, result.TrackName, result.AlbumName);
        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error while setting now playing for bot scrobbling", e);
            throw;
        }

        Statistics.LastfmNowPlayingUpdates.WithLabels(musicBot.Name).Inc();

        var trackScrobbleDelayMs = 60000;
        if (result.DurationMs.HasValue)
        {
            trackScrobbleDelayMs = (int)(result.DurationMs.Value / 2);

            trackScrobbleDelayMs = Math.Clamp(trackScrobbleDelayMs, 30000, 240000);
        }

        this._cache.Set($"now-playing-{user.UserId}", true, TimeSpan.FromMilliseconds(trackScrobbleDelayMs - 1000));

        await Task.Delay(TimeSpan.FromMilliseconds(trackScrobbleDelayMs));

        if (!this._cache.TryGetValue($"now-playing-{user.UserId}", out bool _))
        {
            await this._dataSourceFactory.ScrobbleAsync(user.SessionKeyLastFm, result.ArtistName, result.TrackName, result.AlbumName);
            Statistics.LastfmScrobbles.WithLabels(musicBot.Name).Inc();
        }

    }
    private async Task<List<User>> GetUsersInVoice(ICommandContext context, ulong botId)
    {
        try
        {
            SocketGuildUser guildUser = null;

            if (context.User is not SocketGuildUser resolvedGuildUser)
            {
                // MessageUpdate event returns SocketWebhookUser instead of SocketGuildUser. In order to continue, we
                // need to get the SocketGuildUser instance from the guild object.
                if (context.User is SocketWebhookUser webhookUser)
                {
                    guildUser = await context.Guild.GetUserAsync(webhookUser.Id) as SocketGuildUser;
                }

                if (guildUser is null)
                {
                    Log.Debug("BotScrobbling: Skipped scrobble for {guildName} / {guildId} because no found guild user", context.Guild.Name, context.Guild.Id);
                    this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no found guild user"));
                    return null;
                }
            }
            else
            {
                guildUser = resolvedGuildUser;
            }

            var voiceChannel = guildUser.VoiceChannel;

            if (voiceChannel == null)
            {
                Log.Debug("BotScrobbling: Skipped scrobble for {guildName} / {guildId} because no found voice channels", context.Guild.Name, context.Guild.Id);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no found voice channel"));
                return null;
            }

            if (voiceChannel.ConnectedUsers?.Any() == null)
            {
                Log.Debug("BotScrobbling: Skipped scrobble for {guildName} / {guildId} because no connected users in voice channel", context.Guild.Name, context.Guild.Id, botId);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no connected users in voice channel"));
                return null;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();

            var userIds = voiceChannel.ConnectedUsers
                .Where(w => !w.IsDeafened && !w.IsSelfDeafened)
                .Select(s => s.Id);

            var users = await db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId) &&
                            w.MusicBotTrackingDisabled != true &&
                            w.SessionKeyLastFm != null)
                .ToListAsync();

            Log.Debug("BotScrobbling: Found voice channel {channelName} / {channelId} in {guildName} / {guildId} with {listenerCount} .fmbot listeners", voiceChannel.Name, voiceChannel.Id, context.Guild.Name, context.Guild.Id, users.Count);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Found vc `{voiceChannel.Name}` with `{users.Count}` fmbot listeners"));

            return users;
        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error while getting users in voice", e);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Error while getting vc users"));
            return null;
        }
    }
}
