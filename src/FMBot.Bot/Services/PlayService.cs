using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class PlayService
    {
        public async Task<TimeOverview.WeekOverview> GetUserWeekOverview(User user)
        {
            var week = new TimeOverview.WeekOverview();

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var plays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.UserId == user.UserId).ToListAsync();

            var days = plays
                .OrderByDescending(o => o.TimePlayed)
                .Where(w => w.TimePlayed.Date <= now.Date && w.TimePlayed.Date > minDate.Date)
                .GroupBy(g => g.TimePlayed.Date)
                .Select(s => new TimeOverview.DayOverview
                {
                    Date = s.Key,
                    Playcount = s.Count(),
                    TopTrack = GetTopTrackForPlays(s.ToList()),
                    TopAlbum = GetTopAlbumForPlays(s.ToList()),
                    TopArtist = GetTopArtistForPlays(s.ToList())
                }).ToList();

            week.Days = days;

            return week;
        }

        private static string GetTopTrackForPlays(IEnumerable<UserPlay> plays)
        {
            var tracks = plays
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .OrderByDescending(o => o.Count())
                .ToList();
            return $"`{tracks.Count}` {GetPlaysString(tracks.Count)} - {tracks.First().Key.ArtistName} | {tracks.First().Key.TrackName}";
        }

        private static string GetTopAlbumForPlays(IEnumerable<UserPlay> plays)
        {
            var albums = plays
                .GroupBy(x => new { x.ArtistName, x.AlbumName })
                .OrderByDescending(o => o.Count())
                .ToList();
            return $"`{albums.Count}` {GetPlaysString(albums.Count)} - {albums.First().Key.ArtistName} | {albums.First().Key.AlbumName}";
        }

        private static string GetTopArtistForPlays(IEnumerable<UserPlay> plays)
        {
            var artists = plays
                .GroupBy(x => x.ArtistName)
                .OrderByDescending(o => o.Count())
                .ToList();
            return $"`{artists.Count}` {GetPlaysString(artists.Count)} - {artists.First().Key}";
        }

        private static string GetPlaysString(int playcount)
        {
            return playcount == 1 ? "play" : "plays";
        }
    }
}
