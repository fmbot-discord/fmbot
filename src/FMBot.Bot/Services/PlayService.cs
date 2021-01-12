using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Artist = FMBot.LastFM.Domain.Models.Artist;

namespace FMBot.Bot.Services
{
    public class PlayService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public PlayService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<DailyOverview> GetDailyOverview(User user, int amountOfDays)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-amountOfDays);

            var plays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.TimePlayed.Date <= now.Date &&
                            w.TimePlayed.Date > minDate.Date &&
                            w.UserId == user.UserId)
                .ToListAsync();

            var overview = new DailyOverview
            {
                Days = plays
                    .OrderByDescending(o => o.TimePlayed)
                    .GroupBy(g => g.TimePlayed.Date)
                    .Select(s => new DayOverview
                    {
                        Date = s.Key,
                        Playcount = s.Count(),
                        TopTrack = GetTopTrackForPlays(s.ToList()),
                        TopAlbum = GetTopAlbumForPlays(s.ToList()),
                        TopArtist = GetTopArtistForPlays(s.ToList())
                    }).ToList(),
                Playcount = plays.Count,
                Uniques = GetUniqueCount(plays.ToList()),
                AvgPerDay = GetAvgPerDayCount(plays.ToList()),
            };

            return overview;
        }

        private static int GetUniqueCount(IEnumerable<UserPlay> plays)
        {
            return plays
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Count();
        }

        private static double GetAvgPerDayCount(IEnumerable<UserPlay> plays)
        {
            return plays
                .GroupBy(g => g.TimePlayed.Date)
                .Average(a => a.Count());
        }

        private static string GetTopTrackForPlays(IEnumerable<UserPlay> plays)
        {
            var topTrack = plays
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topTrack == null)
            {
                return "No top track for this day";
            }

            return $"`{topTrack.Count()}` {StringExtensions.GetPlaysString(topTrack.Count())} - {topTrack.Key.ArtistName} | {topTrack.Key.TrackName}";
        }

        private static string GetTopAlbumForPlays(IEnumerable<UserPlay> plays)
        {
            var topAlbum = plays
                .GroupBy(x => new { x.ArtistName, x.AlbumName })
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topAlbum == null)
            {
                return "No top album for this day";
            }

            return $"`{topAlbum.Count()}` {StringExtensions.GetPlaysString(topAlbum.Count())} - {topAlbum.Key.ArtistName} | {topAlbum.Key.AlbumName}";
        }

        private static string GetTopArtistForPlays(IEnumerable<UserPlay> plays)
        {
            var topArtist = plays
                .GroupBy(x => x.ArtistName)
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topArtist == null)
            {
                return "No top artist for this day";
            }

            return $"`{topArtist.Count()}` {StringExtensions.GetPlaysString(topArtist.Count())} - {topArtist.Key}";
        }

        public async Task<int> GetWeekTrackPlaycountAsync(int userId, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.TrackName.ToLower() == trackName.ToLower() &&
                                 t.ArtistName.ToLower() == artistName.ToLower() &&
                                 t.UserId == userId);
        }

        public async Task<int> GetWeekAlbumPlaycountAsync(int userId, string albumName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(ab => ab.TimePlayed.Date <= now.Date &&
                                 ab.TimePlayed.Date > minDate.Date &&
                                 ab.AlbumName.ToLower() == albumName.ToLower() &&
                                 ab.ArtistName.ToLower() == artistName.ToLower() &&
                                 ab.UserId == userId);
        }

        public async Task<int> GetWeekArtistPlaycountAsync(int userId, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(a => a.TimePlayed.Date <= now.Date &&
                                 a.TimePlayed.Date > minDate.Date &&
                                 a.ArtistName.ToLower() == artistName.ToLower() &&
                                 a.UserId == userId);
        }

        public async Task<string> GetStreak(int userId, Response<RecentTrackList> recentTracks)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var lastPlays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.UserId == userId)
                .OrderByDescending(o => o.TimePlayed)
                .ToListAsync();

            if (!lastPlays.Any())
            {
                return null;
            }

            var firstPlay = recentTracks.Content.RecentTracks.First();

            var artistCount = 1;
            var albumCount = 1;
            var trackCount = 1;

            var artistContinue = true;
            var albumContinue = true;
            var trackContinue = true;
            for (var i = 1; i < lastPlays.Count; i++)
            {
                var play = lastPlays[i];

                if (firstPlay.ArtistName.ToLower() == play.ArtistName.ToLower() && artistContinue)
                {
                    artistCount++;
                }
                else
                {
                    artistContinue = false;
                }

                if (firstPlay.AlbumName.ToLower() == play.AlbumName.ToLower() && albumContinue)
                {
                    albumCount++;
                }
                else
                {
                    albumContinue = false;
                }

                if (firstPlay.TrackName.ToLower() == play.TrackName.ToLower() && trackContinue)
                {
                    trackCount++;
                }
                else
                {
                    trackContinue = false;
                }

                if (!artistContinue && !albumContinue && !trackContinue)
                {
                    break;
                }
            }

            var description = new StringBuilder();
            if (artistCount > 1)
            {
                description.AppendLine($"Artist: **[{firstPlay.ArtistName}](https://www.last.fm/music/{HttpUtility.UrlEncode(firstPlay.ArtistName)})** - " +
                                       $"{GetEmojiForStreakCount(artistCount)}*{artistCount} plays in a row*");
            }
            if (albumCount > 1)
            {
                description.AppendLine($"Album: **[{firstPlay.AlbumName}](https://www.last.fm/music/{HttpUtility.UrlEncode(firstPlay.ArtistName)}/{HttpUtility.UrlEncode(firstPlay.AlbumName)})** - " +
                                       $"{GetEmojiForStreakCount(albumCount)}*{albumCount} plays in a row*");
            }
            if (trackCount > 1)
            {
                description.AppendLine($"Track: **[{firstPlay.TrackName}](https://www.last.fm/music/{HttpUtility.UrlEncode(firstPlay.ArtistName)}/_/{HttpUtility.UrlEncode(firstPlay.TrackName)})** - " +
                                       $"{GetEmojiForStreakCount(trackCount)}*{trackCount} plays in a row*");
            }

            if (description.Length > 0)
            {
                return description.ToString();
            }

            return "No active streak found.";
        }

        private string GetEmojiForStreakCount(int count)
        {
            if (count > 50 && count < 100 || count > 100)
            {
                return "🔥 ";
            }

            if (count == 100)
            {
                return "💯 ";
            }

            return null;
        }

        public async Task<Response<TopTracksLfmResponse>> GetTopTracks(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            var tracks = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Select(s => new TopTrackLfm
                {
                    Name = s.Key.TrackName,
                    Artist = new Artist
                    {
                        Name = s.Key.ArtistName
                    },
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();

            return new Response<TopTracksLfmResponse>
            {
                Success = true,
                Content = new TopTracksLfmResponse
                {
                    TopTracks = new TopTracksLfm
                    {
                        Track = tracks
                    }
                }
            };
        }

        public async Task<IReadOnlyList<UserAlbum>> GetTopAlbums(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.AlbumName })
                .Select(s => new UserAlbum
                {
                    Name = s.Key.AlbumName,
                    ArtistName = s.Key.ArtistName,
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<UserArtist>> GetTopArtists(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => x.ArtistName)
                .Select(s => new UserArtist
                {
                    Name = s.Key,
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();
        }


        public async Task<List<UserTrack>> GetTopTracksForArtist(int userId, int days, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                            t.TimePlayed.Date > minDate.Date &&
                            EF.Functions.ILike(t.ArtistName, artistName) &&
                            t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Select(s => new UserTrack
                {
                    ArtistName = s.Key.ArtistName,
                    Name = s.Key.TrackName,
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .Take(10)
                .ToListAsync();
        }
    }
}
