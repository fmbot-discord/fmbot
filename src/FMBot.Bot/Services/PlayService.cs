using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class PlayService
    {
        public async Task<DailyOverview> GetDailyOverview(User user, int amountOfDays)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

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

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(a => a.TimePlayed.Date <= now.Date &&
                                 a.TimePlayed.Date > minDate.Date &&
                                 a.ArtistName.ToLower() == artistName.ToLower() &&
                                 a.UserId == userId);
        }
    }
}
