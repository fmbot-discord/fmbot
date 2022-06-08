using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class MusicBotService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly LastFmRepository _lastFmRepository;
    private readonly TrackService _trackService;
    private readonly IMemoryCache _cache;
    private readonly IPrefixService _prefixService;
    public readonly List<BotScrobblingLog> BotScrobblingLogs;

    public record BotScrobblingLog(ulong GuildId, DateTime DateTime, string Log);

    private InteractiveService Interactivity { get; }

    public MusicBotService(IDbContextFactory<FMBotDbContext> contextFactory, LastFmRepository lastFmRepository, TrackService trackService, IMemoryCache cache, InteractiveService interactivity, IPrefixService prefixService)
    {
        this._contextFactory = contextFactory;
        this._lastFmRepository = lastFmRepository;
        this._trackService = trackService;
        this._cache = cache;
        this.Interactivity = interactivity;
        this._prefixService = prefixService;
        this.BotScrobblingLogs = new List<BotScrobblingLog>();
    }

    public async Task ScrobbleHydra(SocketUserMessage msg, ICommandContext context)
    {
        try
        {
            if (context.Guild == null ||
                msg.Embeds == null ||
                !msg.Embeds.Any() ||
                msg.Embeds.Any(a => a.Title == null) ||
                (msg.Embeds.Any(a => a.Title != "Now playing") && msg.Embeds.Any(a => a.Title != "Speelt nu")) ||
                msg.Embeds.Any(a => a.Description == null))
            {
                return;
            }

            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, "Found 'now playing' message from Hydra"));

            var usersInChannel = await GetUsersInVoice(context, msg.Author.Id);

            if (usersInChannel == null || usersInChannel.Count == 0)
            {
                return;
            }

            var trackResult = await this._trackService.GetTrackFromLink(msg.Embeds.First().Description, false);

            if (trackResult == null)
            {
                Log.Information("BotScrobbling: Skipped scrobble for {listenerCount} users in {guildName} / {guildId} because no found track for {trackDescription}", usersInChannel.Count, context.Guild.Name, context.Guild.Id, msg.Embeds.First().Description);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no found track for `{msg.Embeds.First().Description}`"));
                return;
            }

            _ = RegisterTrack(usersInChannel, trackResult);

            _ = SendScrobbleMessage(context, trackResult, usersInChannel.Count);
        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error in music bot scrobbler (hydra)", e);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, "Skipped scrobble because error"));
        }
    }

    public async Task ScrobbleCakeyBot(SocketUserMessage msg, ICommandContext context)
    {
        try
        {
            if (context.Guild == null ||
                msg.Embeds == null ||
                !msg.Embeds.Any() ||
                msg.Embeds.Any(a => a.Title == null) ||
                msg.Embeds.Any(a => a.Description == null) ||
                !msg.Embeds.Any(a => a.Description.Contains("Now playing:")))
            {
                return;
            }

            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, "Found 'now playing' message from Cakey"));

            var usersInChannel = await GetUsersInVoice(context, msg.Author.Id);

            if (usersInChannel == null || usersInChannel.Count == 0)
            {
                return;
            }

            var pFrom = msg.Embeds.First().Description.IndexOf("Now playing: ", StringComparison.Ordinal) + "Now playing: ".Length;
            var pTo = msg.Embeds.First().Description.LastIndexOf(" [", StringComparison.Ordinal);

            var result = msg.Embeds.First().Description.Substring(pFrom, pTo - pFrom);

            var trackResult = await this._trackService.GetTrackFromLink(result);

            if (trackResult == null)
            {
                Log.Information("BotScrobbling: Skipped scrobble for {listenerCount} users in {guildName} / {guildId} because no found track for {trackDescription}", usersInChannel.Count, context.Guild.Name, context.Guild.Id, msg.Embeds.First().Description);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no found track for `{msg.Embeds.First().Description}`"));
                return;
            }

            _ = RegisterTrack(usersInChannel, trackResult);

            _ = SendScrobbleMessage(context, trackResult, usersInChannel.Count);
        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error in music bot scrobbler (Cakey Bot)", e);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because error"));
        }
    }

    private async Task SendScrobbleMessage(ICommandContext context, TrackSearchResult trackResult,
        int listenerCount)
    {
        var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
        var prfx = this._prefixService.GetPrefix(context.Guild?.Id);

        embed.WithDescription(
            $"Scrobbling **{trackResult.TrackName}** by **{trackResult.ArtistName}** for {listenerCount} {StringExtensions.GetListenersString(listenerCount)}");
        embed.WithFooter($"Use '{prfx}botscrobbling' for more information.");

        this.Interactivity.DelayedDeleteMessageAsync(
            await context.Channel.SendMessageAsync(embed: embed.Build()),
            TimeSpan.FromSeconds(120));

        Log.Debug("BotScrobbling: Scrobbled {trackName} by {artistName} for {listenerCount} users in {guildName} / {guildId}", trackResult.TrackName, trackResult.ArtistName, listenerCount, context.Guild.Name, context.Guild.Id);
        this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Scrobbled `{trackResult.TrackName}` by `{trackResult.ArtistName}`"));
    }


    private async Task RegisterTrack(IEnumerable<User> users, TrackSearchResult result)
    {
        foreach (var user in users)
        {
            _ = RegisterTrackForUser(user, result);
        }
    }

    private async Task RegisterTrackForUser(User user, TrackSearchResult result)
    {
        try
        {
            await this._lastFmRepository.SetNowPlayingAsync(user, result.ArtistName, result.TrackName, result.AlbumName);
        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error while setting now playing for bot scrobbling", e);
            throw;
        }
        Statistics.LastfmNowPlayingUpdates.Inc();

        this._cache.Set($"now-playing-{user.UserId}", true, TimeSpan.FromSeconds(59));
        await Task.Delay(TimeSpan.FromSeconds(60));

        if (!this._cache.TryGetValue($"now-playing-{user.UserId}", out bool _))
        {
            await this._lastFmRepository.ScrobbleAsync(user, result.ArtistName, result.TrackName, result.AlbumName);
            Statistics.LastfmScrobbles.Inc();
        }

    }
    private async Task<List<User>> GetUsersInVoice(ICommandContext context, ulong botId)
    {
        try
        {
            if (await context.Guild.GetVoiceChannelsAsync() is not ImmutableArray<SocketVoiceChannel> channels || !channels.Any())
            {
                Log.Debug("BotScrobbling: Skipped scrobble for {guildName} / {guildId} because no found voice channels", context.Guild.Name, context.Guild.Id);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no found voice channels"));
                return null;
            }

            var targetChannel = channels.FirstOrDefault(f => f.Users != null &&
                                                             f.Users.Any(a => a.Id == botId));

            if (targetChannel == null)
            {
                Log.Debug("BotScrobbling: Skipped scrobble for {guildName} / {guildId} because no found voice channel with botId {botId} (did see some voice channels though)", context.Guild.Name, context.Guild.Id, botId);
                this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Skipped scrobble because no voice channel with the music bot in it"));
                return null;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();

            var userIds = targetChannel.ConnectedUsers.Select(s => s.Id);

            var users = await db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId) &&
                            w.MusicBotTrackingDisabled != true &&
                            w.SessionKeyLastFm != null)
                .ToListAsync();

            Log.Debug("BotScrobbling: Found voice channel {channelName} / {channelId} in {guildName} / {guildId} with {listenerCount} .fmbot listeners", targetChannel.Name, targetChannel.Id, context.Guild.Name, context.Guild.Id, users.Count);
            this.BotScrobblingLogs.Add(new BotScrobblingLog(context.Guild.Id, DateTime.UtcNow, $"Found vc `{targetChannel.Name}` with `{users.Count}` fmbot listeners"));

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
