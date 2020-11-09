using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsPlayService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;

        public WhoKnowsPlayService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        public async Task<IReadOnlyList<ListArtist>> GetTopWeekArtistsForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();

            var artistUserPlays = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                            t.TimePlayed.Date > minDate.Date &&
                            userIds.Contains(t.UserId))
                .GroupBy(x => new { x.ArtistName, x.UserId })
                .Select(s => new ArtistUserPlay
                {
                    ArtistName = s.Key.ArtistName,
                    UserId = s.Key.UserId,
                    Playcount = s.Count()
                })
                .ToListAsync();

            var query = artistUserPlays
                .GroupBy(g => g.ArtistName)
                .Select(s => new ListArtist
                {
                    ArtistName = s.Key,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count()
                });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Playcount).ThenByDescending(o => o.Playcount) :
                query.OrderByDescending(o => o.ListenerCount).ThenByDescending(o => o.Playcount);

            return query
                .Take(14)
                .ToList();
        }

        public async Task<IReadOnlyList<ListAlbum>> GetTopWeekAlbumsForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();

            var albumUserPlays = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                            t.TimePlayed.Date > minDate.Date &&
                            userIds.Contains(t.UserId))
                .GroupBy(x => new { x.ArtistName, x.AlbumName, x.UserId })
                .Select(s => new AlbumUserPlay
                {
                    ArtistName = s.Key.ArtistName,
                    AlbumName = s.Key.AlbumName,
                    UserId = s.Key.UserId,
                    Playcount = s.Count()
                })
                .ToListAsync();

            var query = albumUserPlays
                .GroupBy(g => new { g.ArtistName, g.AlbumName })
                .Select(s => new ListAlbum
                {
                    ArtistName = s.Key.ArtistName,
                    AlbumName = s.Key.AlbumName,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count()
                });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Playcount).ThenByDescending(o => o.Playcount) :
                query.OrderByDescending(o => o.ListenerCount).ThenByDescending(o => o.Playcount);

            return query
                .Take(14)
                .ToList();
        }

        public async Task<IReadOnlyList<ListTrack>> GetTopWeekTracksForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();

            var trackUserPlays = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                            t.TimePlayed.Date > minDate.Date &&
                            userIds.Contains(t.UserId))
                .GroupBy(x => new { x.ArtistName, x.TrackName, x.UserId })
                .Select(s => new TrackUserPlay
                {
                    ArtistName = s.Key.ArtistName,
                    TrackName = s.Key.TrackName,
                    UserId = s.Key.UserId,
                    Playcount = s.Count()
                })
                .ToListAsync();

            var query = trackUserPlays
                .GroupBy(g => new { g.ArtistName, g.TrackName })
                .Select(s => new ListTrack
                {
                    ArtistName = s.Key.ArtistName,
                    TrackName = s.Key.TrackName,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count()
                });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Playcount).ThenByDescending(o => o.Playcount) :
                query.OrderByDescending(o => o.ListenerCount).ThenByDescending(o => o.Playcount);

            return query
                .Take(14)
                .ToList();
        }

        public void AddRecentPlayToCache(int userId, LastTrack track)
        {
            if (track.IsNowPlaying == true || track.TimePlayed.HasValue && track.TimePlayed.Value > DateTime.UtcNow.AddMinutes(-8))
            {
                var userPlay = new UserPlay
                {
                    ArtistName = track.ArtistName.ToLower(),
                    AlbumName = track.AlbumName.ToLower(),
                    TrackName = track.Name.ToLower(),
                    UserId = userId,
                    TimePlayed = track.TimePlayed?.DateTime ?? DateTime.UtcNow
                };

                this._cache.Set($"{userId}-last-play-{track.ArtistName.ToLower()}-{track.Name.ToLower()}", userPlay, TimeSpan.FromMinutes(10));
            }
        }

        public async Task<string> GuildAlsoPlaying(int userId, ulong discordGuildId, LastTrack track)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId != userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild == null || !guild.GuildUsers.Any())
            {
                return null;
            }

            var foundUsers = new List<GuildUser>();
            var userPlays = new List<UserPlay>();

            foreach (var user in guild.GuildUsers)
            {
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play-{track.ArtistName.ToLower()}-{track.Name.ToLower()}", out UserPlay userPlay);

                if (userFound)
                {
                    foundUsers.Add(user);
                    userPlays.Add(userPlay);
                }
            }

            if (!foundUsers.Any())
            {
                return null;
            }

            if (foundUsers.Count == 1)
            {
                return $"{foundUsers.First().UserName} was also listening to this track {GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this track!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this track!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this track!";
            }

            return null;
        }

        private string GetTimeAgo(DateTime timeAgo)
        {
            var ts = new TimeSpan(DateTime.UtcNow.Ticks - timeAgo.Ticks);
            var delta = Math.Abs(ts.TotalSeconds);

            if (delta < 60)
            {
                return ts.Seconds == 1 ? "one second ago" : ts.Seconds + " seconds ago";
            }
            if (delta < 60 * 2)
            {
                return "a minute ago";
            }
            if (delta < 45 * 60)
            {
                return ts.Minutes + " minutes ago";
            }
            if (delta < 90 * 60)
            {
                return "an hour ago";
            }
            if (delta < 24 * 60 * 60)
            {
                return ts.Hours + " hours ago";
            }

            return "more then a day ago";
        }

        private class ArtistUserPlay
        {
            public string ArtistName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }

        private class AlbumUserPlay
        {
            public string ArtistName { get; set; }

            public string AlbumName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }

        private class TrackUserPlay
        {
            public string ArtistName { get; set; }

            public string TrackName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }
    }
}
