using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
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
                .Where(t =>
                    userIds.Contains(t.UserId) &&
                    t.TimePlayed.Date <= now.Date &&
                    t.TimePlayed.Date > minDate.Date)
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
                .Where(t =>
                    userIds.Contains(t.UserId) &&
                    t.TimePlayed.Date <= now.Date &&
                    t.TimePlayed.Date > minDate.Date)
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
                .Where(t =>
                    userIds.Contains(t.UserId) &&
                    t.TimePlayed.Date <= now.Date &&
                    t.TimePlayed.Date > minDate.Date)
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

        public void AddRecentPlayToCache(int userId, RecentTrack track)
        {
            if (track.NowPlaying || track.TimePlayed != null && track.TimePlayed > DateTime.UtcNow.AddMinutes(-8))
            {
                var userPlay = new UserPlay
                {
                    ArtistName = track.ArtistName,
                    AlbumName = track.AlbumName,
                    TrackName = track.TrackName,
                    UserId = userId,
                    TimePlayed = track.TimePlayed ?? DateTime.UtcNow
                };

                this._cache.Set($"{userId}-last-play", userPlay, TimeSpan.FromMinutes(15));
            }
        }

        public async Task<string> GuildAlsoPlayingTrack(int userId, ulong discordGuildId, string artistName, string trackName)
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
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.TrackName == trackName.ToLower())
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
                return $"{foundUsers.First().UserName} was also listening to this trackLfm {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this trackLfm!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this trackLfm!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this trackLfm!";
            }

            return null;
        }

        public async Task<string> GuildAlsoPlayingAlbum(int userId, ulong discordGuildId, string artistName, string albumName)
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
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.AlbumName == albumName.ToLower())
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
                return $"{foundUsers.First().UserName} was also listening to this album {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this album!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this album!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this album!";
            }

            return null;
        }

        public async Task<string> GuildAlsoPlayingArtist(int userId, ulong discordGuildId, string artistName)
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
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.TimePlayed > DateTime.UtcNow.AddMinutes(-10))
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
                return $"{foundUsers.First().UserName} was also listening to this artist {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this artist!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this artist!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this artist!";
            }

            return null;
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
