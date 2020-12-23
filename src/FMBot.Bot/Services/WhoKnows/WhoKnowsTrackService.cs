using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsTrackService
    {
        private readonly IMemoryCache _cache;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsTrackService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(ICommandContext context,
            ICollection<GuildUser> guildUsers, string artistName, string trackName)
        {
            var cachedUserTracks = new List<CachedUserTrack>();
            foreach (var user in guildUsers)
            {
                var key = $"top-tracks-{user.UserId}";

                if (this._cache.TryGetValue(key, out List<CachedUserTrack> topTracksForUser))
                {
                    var userTrack = topTracksForUser.FirstOrDefault(f => f.Name.ToLower() == trackName.ToLower() &&
                                                                          f.ArtistName.ToLower() == artistName.ToLower());

                    if (userTrack != null)
                    {
                        cachedUserTracks.Add(userTrack);
                    }
                }
                else
                {
                    await using var db = this._contextFactory.CreateDbContext();
                    var userTracksToCache = await db.UserTracks
                        .AsQueryable()
                        .Include(i => i.User)
                        .Where(w => w.UserId == user.UserId)
                        .Select(s => new CachedUserTrack
                        {
                            Name = s.Name,
                            ArtistName = s.ArtistName,
                            Playcount = s.Playcount,
                            LastFmUserName = s.User.UserNameLastFM,
                            UserId = s.UserId,
                            DiscordUserId = s.User.DiscordUserId
                        })
                        .ToListAsync();

                    if (userTracksToCache != null && userTracksToCache.Any())
                    {
                        this._cache.Set(key, userTracksToCache, TimeSpan.FromMinutes(20));

                        var userTrack = userTracksToCache.FirstOrDefault(f => f.Name.ToLower() == trackName.ToLower() &&
                                                                              f.ArtistName.ToLower() == artistName.ToLower());

                        if (userTrack != null)
                        {
                            cachedUserTracks.Add(userTrack);
                        }
                    }
                }
            }

            var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

            foreach (var userTrack in cachedUserTracks)
            {
                var discordUser = await context.Guild.GetUserAsync(userTrack.DiscordUserId);
                var guildUser = guildUsers.FirstOrDefault(f => f.UserId == userTrack.UserId);
                var userName = discordUser != null ?
                    discordUser.Nickname ?? discordUser.Username :
                    guildUser?.UserName ?? userTrack.LastFmUserName;

                whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userTrack.ArtistName} - {userTrack.Name}",
                    DiscordName = userName,
                    Playcount = userTrack.Playcount,
                    LastFMUsername = userTrack.LastFmUserName,
                    UserId = userTrack.UserId,
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<IReadOnlyList<ListTrack>> GetTopTracksForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var query = db.UserTracks
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(g => new { g.ArtistName, g.Name });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Sum(s => s.Playcount)).ThenByDescending(o => o.Count()) :
                query.OrderByDescending(o => o.Count()).ThenByDescending(o => o.Sum(s => s.Playcount));

            return await query
                .Take(14)
                .Select(s => new ListTrack
                {
                    ArtistName = s.Key.ArtistName,
                    TrackName = s.Key.Name,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }

        public async Task<int> GetWeekTrackPlaycountForGuildAsync(IEnumerable<User> guildUsers, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(t =>
                    userIds.Contains(t.UserId) &&
                    t.TimePlayed.Date <= now.Date &&
                    t.TimePlayed.Date > minDate.Date &&
                    t.TrackName.ToLower() == trackName.ToLower() &&
                    t.ArtistName.ToLower() == artistName.ToLower()
                    );
        }
    }
}
