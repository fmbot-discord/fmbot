using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
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

        public MusicBotService(IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService, TrackService trackService, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
            this._trackService = trackService;
            this._cache = cache;
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

            await RegisterTrack(usersInChannel, trackResult);
        }

        private async Task RegisterTrack(List<User> users, TrackSearchResult result)
        {
            foreach (var user in users)
            {
                var nowPlayingResult = await this._lastFmService.SetNowPlayingAsync(user, result.ArtistName, result.TrackName, result.AlbumName);

                this._cache.Set($"now-playing-{user.UserId}", true, TimeSpan.FromSeconds(59));
                Thread.Sleep(60000);

                if (!this._cache.TryGetValue($"now-playing-{user.UserId}", out bool _))
                {
                    await this._lastFmService.ScrobbleAsync(user, result.ArtistName, result.TrackName, result.AlbumName);
                }
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
