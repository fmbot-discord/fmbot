using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services
{
    public class MusicBotService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly LastFmService _lastFmService;
        private readonly TrackService _trackService;
        private readonly IMemoryCache _cache;
        private readonly IPrefixService _prefixService;

        private InteractivityService Interactivity { get; }

        public MusicBotService(IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService, TrackService trackService, IMemoryCache cache, InteractivityService interactivity, IPrefixService prefixService)
        {
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
            this._trackService = trackService;
            this._cache = cache;
            this.Interactivity = interactivity;
            this._prefixService = prefixService;
        }

        public async Task ScrobbleGroovy(SocketUserMessage msg, ICommandContext context)
        {
            if (msg.Embeds == null || msg.Embeds.Any(a => a.Title == null || a.Title != "Now playing"))
            {
                return;
            }

            var usersInChannel = await GetUsersInVoice(context, msg.Author.Id);

            if (usersInChannel == null || usersInChannel.Count == 0)
            {
                return;
            }

            var trackResult = await this._trackService.GetTrackFromLink(msg.Embeds.First().Description);

            if (trackResult == null)
            {
                return;
            }

            _ = RegisterTrack(usersInChannel, trackResult);

            var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
            var prfx = this._prefixService.GetPrefix(context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            embed.WithDescription(
                $"Scrobbling **{trackResult.TrackName}** by **{trackResult.ArtistName}** for {usersInChannel.Count} {StringExtensions.GetListenersString(usersInChannel.Count)}");
            embed.WithFooter($"Use '{prfx}botscrobbling' for more information.");

            this.Interactivity.DelayedDeleteMessageAsync(
                await context.Channel.SendMessageAsync(embed: embed.Build()),
                TimeSpan.FromSeconds(60));

            Log.Information("Scrobbled {trackName} by {artistName} for {listenerCount} users in {guildName} / {guildId}", trackResult.TrackName, trackResult.ArtistName, usersInChannel.Count, context.Guild.Name, context.Guild.Name);
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
            await this._lastFmService.SetNowPlayingAsync(user, result.ArtistName, result.TrackName, result.AlbumName);

            this._cache.Set($"now-playing-{user.UserId}", true, TimeSpan.FromSeconds(59));
            await Task.Delay(TimeSpan.FromSeconds(60));

            if (!this._cache.TryGetValue($"now-playing-{user.UserId}", out bool _))
            {
                await this._lastFmService.ScrobbleAsync(user, result.ArtistName, result.TrackName, result.AlbumName);
            }
        } 

        private async Task<List<User>> GetUsersInVoice(ICommandContext context, ulong botId)
        {
            try
            {
                if (!(await context.Guild.GetVoiceChannelsAsync() is ImmutableArray<SocketVoiceChannel> channels) || !channels.Any())
                {
                    return null;
                }

                var targetChannel = channels.FirstOrDefault(f => f.Users != null &&
                                                        f.Users.Any() &&
                                                        f.Users.Select(s => s.Id).Contains(botId));
                if (targetChannel == null)
                {
                    return null;
                }

                await using var db = this._contextFactory.CreateDbContext();

                var userIds = targetChannel.Users.Select(s => s.Id);

                return await db.Users
                    .AsQueryable()
                    .Where(w => userIds.Contains(w.DiscordUserId) &&
                                w.MusicBotTrackingDisabled != true &&
                                w.SessionKeyLastFm != null)
                    .ToListAsync();
            }
            catch (Exception e)
            {
                Log.Error("Error while getting users in voice", e);
                return null;
            }
        }
    }
}
