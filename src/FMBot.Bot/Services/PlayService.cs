using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Artist = FMBot.LastFM.Domain.Models.Artist;

namespace FMBot.Bot.Services
{
    public class PlayService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly GenreService _genreService;
        private readonly TimeService _timeService;
        private readonly BotSettings _botSettings;
        private readonly LastFmRepository _lastFmRepository;
        private readonly IMemoryCache _cache;

        public PlayService(IDbContextFactory<FMBotDbContext> contextFactory, GenreService genreService, TimeService timeService, IOptions<BotSettings> botSettings, LastFmRepository lastFmRepository, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._genreService = genreService;
            this._timeService = timeService;
            this._lastFmRepository = lastFmRepository;
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public async Task<DailyOverview> GetDailyOverview(int userId, int amountOfDays)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-amountOfDays);

            var plays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.TimePlayed.Date <= now.Date &&
                            w.TimePlayed.Date > minDate.Date &&
                            w.UserId == userId)
                .ToListAsync();

            if (!plays.Any())
            {
                return null;
            }

            var overview = new DailyOverview
            {
                Days = plays
                    .OrderByDescending(o => o.TimePlayed)
                    .GroupBy(g => g.TimePlayed.Date)
                    .Select(s => new DayOverview
                    {
                        Date = s.Key,
                        Playcount = s.Count(),
                        Plays = s.ToList(),
                        TopTrack = GetTopTrackForPlays(s.ToList()),
                        TopAlbum = GetTopAlbumForPlays(s.ToList()),
                        TopArtist = GetTopArtistForPlays(s.ToList()),
                    }).Take(amountOfDays).ToList(),
                Playcount = plays.Count,
                Uniques = GetUniqueCount(plays.ToList()),
                AvgPerDay = GetAvgPerDayCount(plays.ToList()),
            };

            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.TopGenres = await this._genreService.GetTopGenresForPlays(day.Plays);
            }
            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.ListeningTime = await this._timeService.GetPlayTimeForPlays(day.Plays);
            }

            return overview;
        }

        public async Task<YearOverview> GetYear(int userId, int year)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users
                 .AsNoTracking()
                 .FirstOrDefaultAsync(f => f.UserId == userId);

            var cacheKey = $"year-ov-{user.UserId}-{year}";

            var cachedYearAvailable = this._cache.TryGetValue(cacheKey, out YearOverview cachedYearOverview);
            if (cachedYearAvailable)
            {
                return cachedYearOverview;
            }

            var startDateTime = new DateTime(year, 01, 01);
            var endDateTime = startDateTime.AddYears(1).AddSeconds(-1);

            var yearOverview = new YearOverview
            {
                Year = year,
                LastfmErrors = false
            };

            var currentTopTracks =
                await this._lastFmRepository.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime, endDateTime, 500, user.SessionKeyLastFm);

            if (!currentTopTracks.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopTracks = currentTopTracks.Content;

            var currentTopAlbums =
                await this._lastFmRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime, endDateTime, 500, user.SessionKeyLastFm);

            if (!currentTopAlbums.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopAlbums = currentTopAlbums.Content;

            var currentTopArtists =
                await this._lastFmRepository.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM, startDateTime, endDateTime, 500, user.SessionKeyLastFm);

            if (!currentTopArtists.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopArtists = currentTopArtists.Content;

            if (user.RegisteredLastFm < endDateTime.AddYears(-1))
            {
                var previousTopTracks =
                    await this._lastFmRepository.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime.AddYears(-2), endDateTime.AddYears(-1), 800, user.SessionKeyLastFm);

                if (previousTopTracks.Success)
                {
                    yearOverview.PreviousTopTracks = previousTopTracks.Content;
                }

                var previousTopAlbums =
                    await this._lastFmRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime.AddYears(-2), endDateTime.AddYears(-1), 800, user.SessionKeyLastFm);

                if (previousTopAlbums.Success)
                {
                    yearOverview.PreviousTopAlbums = previousTopAlbums.Content;
                }

                var previousTopArtists =
                    await this._lastFmRepository.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM, startDateTime.AddYears(-2), endDateTime.AddYears(-1), 800, user.SessionKeyLastFm);

                if (previousTopArtists.Success)
                {
                    yearOverview.PreviousTopArtists = previousTopArtists.Content;
                }
            }

            if (!yearOverview.LastfmErrors)
            {
                this._cache.Set(cacheKey, yearOverview, TimeSpan.FromHours(1));
            }

            return yearOverview;
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

            var streakStarted = new DateTime?();

            var artistContinue = true;
            var albumContinue = true;
            var trackContinue = true;
            for (var i = 1; i < lastPlays.Count; i++)
            {
                var play = lastPlays[i];

                if (firstPlay.ArtistName.ToLower() == play.ArtistName.ToLower() && artistContinue)
                {
                    artistCount++;
                    streakStarted = play.TimePlayed;
                }
                else
                {
                    artistContinue = false;
                }

                if (firstPlay.AlbumName != null && play.AlbumName != null && firstPlay.AlbumName.ToLower() == play.AlbumName.ToLower() && albumContinue)
                {
                    albumCount++;
                    streakStarted = play.TimePlayed;
                }
                else
                {
                    albumContinue = false;
                }

                if (firstPlay.TrackName.ToLower() == play.TrackName.ToLower() && trackContinue)
                {
                    trackCount++;
                    streakStarted = play.TimePlayed;
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

            if (streakStarted.HasValue)
            {
                var specifiedDateTime = DateTime.SpecifyKind(streakStarted.Value, DateTimeKind.Utc);
                var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                description.AppendLine();
                description.AppendLine($"Streak started <t:{dateValue}:R>.");
            }

            if (description.Length > 0)
            {
                return description.ToString();
            }

            return "No active streak found.";
        }

        private static string GetEmojiForStreakCount(int count)
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

        public async Task<TopArtistList> GetTopArtists(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            var topArtists = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => x.ArtistName)
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key,
                    UserPlaycount = s.Count()
                })
                .OrderByDescending(o => o.UserPlaycount)
                .ToListAsync();

            return new TopArtistList
            {
                TotalAmount = topArtists.Count,
                TopArtists = topArtists
            };
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
                .ToListAsync();
        }

        public async Task<List<WhoKnowsObjectWithUser>> GetGuildUsersTotalPlaycount(ICommandContext context, int guildId)
        {
            const string sql = "SELECT u.total_playcount AS playcount, " +
                               "u.user_id, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM users AS u " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND u.total_playcount is not null " +
                               "ORDER BY u.total_playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userAlbums = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
            {
                guildId,
            })).ToList();

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userAlbums.Count; i++)
            {
                var userAlbum = userAlbums[i];

                var userName = userAlbum.UserName ?? userAlbum.UserNameLastFm;

                if (i <= 10)
                {
                    var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.UserNameLastFm,
                    UserId = userAlbum.UserId,
                    WhoKnowsWhitelisted = userAlbum.WhoKnowsWhitelisted,
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<List<UserPlay>> GetGuildUsersTotalPlaytime(int guildId)
        {
            const string sql = "SELECT user_play_id, up.user_id, up.track_name, up.album_name, up.artist_name, up.time_played " +
                               "FROM public.user_plays AS up " +
                               "INNER JOIN users AS u ON up.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId " +
                               "AND time_played > current_date - interval '9' day  AND time_played < current_date - interval '2' day  " +
                               "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                               "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userPlays = (await connection.QueryAsync<UserPlay>(sql, new
            {
                guildId,
            })).ToList();

            return userPlays;
        }
    }
}
