using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Dapper;

using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class PlayService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly GenreService _genreService;
    private readonly TimeService _timeService;
    private readonly BotSettings _botSettings;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly IMemoryCache _cache;

    public PlayService(IDbContextFactory<FMBotDbContext> contextFactory, GenreService genreService,
        TimeService timeService, IOptions<BotSettings> botSettings, IDataSourceFactory dataSourceFactory,
        IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._genreService = genreService;
        this._timeService = timeService;
        this._dataSourceFactory = dataSourceFactory;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public async Task<DailyOverview> GetDailyOverview(int userId, TimeZoneInfo timeZone, int amountOfDays)
    {
        try
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var start = DateTime.UtcNow.AddDays(-amountOfDays);
            var importUser = await UserRepository.GetImportUserForUserId(userId, connection);
            var plays = await PlayRepository.GetUserPlays(userId, connection,
                importUser?.DataSource ?? DataSource.LastFm, start: start);

            if (!plays.Any())
            {
                return null;
            }

            var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(plays);

            var overview = new DailyOverview
            {
                Days = []
            };

            foreach (var day in enrichedPlays.enrichedPlays
                         .OrderBy(o => o.TimePlayed)
                         .GroupBy(g => TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Date))
            {
                overview.Days.Add(new DayOverview
                {
                    Date = day.Key,
                    Playcount = day.Count(),
                    Plays = day.ToList(),
                    TopTrack = GetTopTrackForPlays(day.ToList()),
                    TopAlbum = GetTopAlbumForPlays(day.ToList()),
                    TopArtist = GetTopArtistForPlays(day.ToList()),
                });
            }

            var allArtistIds = enrichedPlays.enrichedPlays
                .Where(p => p.ArtistId.HasValue)
                .Select(p => p.ArtistId.Value);
            var genreMap = await this._genreService.GetGenresByArtistIds(allArtistIds);

            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.TopGenres = GenreService.GetTopGenresFromPlays(day.Plays, genreMap);
            }

            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.ListeningTime = TimeService.GetPlayTimeForEnrichedPlays(day.Plays);
            }

            return overview;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error getting daily overview for user {UserId}", userId);
            throw;
        }
    }

    public async Task<YearOverview> GetYear(int userId, int year)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsNoTracking()
            .FirstAsync(f => f.UserId == userId);

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

        var currentTopTracksTask =
            this._dataSourceFactory.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime,
                endDateTime, 500);
        var currentTopAlbumsTask =
            this._dataSourceFactory.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime,
                endDateTime, 500);
        var currentTopArtistsTask =
            this._dataSourceFactory.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM, startDateTime,
                endDateTime, 500);

        var currentTopTracks = await currentTopTracksTask;
        var currentTopAlbums = await currentTopAlbumsTask;
        var currentTopArtists = await currentTopArtistsTask;

        if (!currentTopTracks.Success || !currentTopAlbums.Success || !currentTopArtists.Success)
        {
            yearOverview.LastfmErrors = true;
            return yearOverview;
        }

        yearOverview.TopTracks = currentTopTracks.Content;
        yearOverview.TopAlbums = currentTopAlbums.Content;
        yearOverview.TopArtists = currentTopArtists.Content;

        if (user.RegisteredLastFm < endDateTime.AddYears(-1))
        {
            var previousTopTracksTask =
                this._dataSourceFactory.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM,
                    startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);
            var previousTopAlbumsTask =
                this._dataSourceFactory.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM,
                    startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);
            var previousTopArtistsTask =
                this._dataSourceFactory.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM,
                    startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);

            var previousTopTracks = await previousTopTracksTask;
            var previousTopAlbums = await previousTopAlbumsTask;
            var previousTopArtists = await previousTopArtistsTask;

            if (previousTopTracks.Success)
            {
                yearOverview.PreviousTopTracks = previousTopTracks.Content;
            }
            else
            {
                yearOverview.LastfmErrors = true;
            }

            if (previousTopAlbums.Success)
            {
                yearOverview.PreviousTopAlbums = previousTopAlbums.Content;
            }
            else
            {
                yearOverview.LastfmErrors = true;
            }

            if (previousTopArtists.Success)
            {
                yearOverview.PreviousTopArtists = previousTopArtists.Content;
            }
            else
            {
                yearOverview.LastfmErrors = true;
            }
        }

        if (!yearOverview.LastfmErrors)
        {
            this._cache.Set(cacheKey, yearOverview, TimeSpan.FromHours(3));
        }

        return yearOverview;
    }

    public static int GetUniqueCount(IEnumerable<UserPlay> plays)
    {
        return plays
            .GroupBy(x => new { x.ArtistName, x.TrackName })
            .Count();
    }

    public static double GetAvgPerDayCount(DayOverview[] days)
    {
        return days.Length != 0 ? Math.Round(days.Average(d => d.Playcount), 1) : 0;
    }

    private static string GetTopTrackForPlays(IEnumerable<UserPlay> plays)
    {
        var topTrack = plays
            .GroupBy(x => new { x.ArtistName, x.TrackName })
            .MaxBy(o => o.Count());

        if (topTrack == null)
        {
            return "No top track for this day";
        }

        return
            $"{StringExtensions.Sanitize(topTrack.Key.ArtistName)} - {StringExtensions.Sanitize(topTrack.Key.TrackName)} — *{topTrack.Count()} {StringExtensions.GetPlaysString(topTrack.Count())}*";
    }

    private static string GetTopAlbumForPlays(IEnumerable<UserPlay> plays)
    {
        var topAlbum = plays
            .GroupBy(x => new { x.ArtistName, x.AlbumName })
            .MaxBy(o => o.Count());

        if (topAlbum == null)
        {
            return "No top album for this day";
        }

        return
            $"{StringExtensions.Sanitize(topAlbum.Key.ArtistName)} - {StringExtensions.Sanitize(topAlbum.Key.AlbumName)} — *{topAlbum.Count()} {StringExtensions.GetPlaysString(topAlbum.Count())}*";
    }

    private static string GetTopArtistForPlays(IEnumerable<UserPlay> plays)
    {
        var topArtist = plays
            .GroupBy(x => x.ArtistName)
            .MaxBy(o => o.Count());

        if (topArtist == null)
        {
            return "No top artist for this day";
        }

        return
            $"{StringExtensions.Sanitize(topArtist.Key)} — *{topArtist.Count()} {StringExtensions.GetPlaysString(topArtist.Count())}*";
    }

    private async Task<ICollection<UserPlay>> GetWeekPlays(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var start = DateTime.UtcNow.AddDays(-7);
        return await PlayRepository.GetUserPlaysWithinTimeRange(userId, connection, start);
    }

    public async Task<(int week, int month)> GetRecentTrackPlaycounts(int userId, string trackName, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var monthAgo = DateTime.UtcNow.AddMonths(-1);

        var importUser = await UserRepository.GetImportUserForUserId(userId, connection);
        var plays = await PlayRepository.GetUserPlays(userId, connection, importUser?.DataSource ?? DataSource.LastFm,
            start: monthAgo);

        return (
            plays.Count(c =>
                c.TimePlayed >= weekAgo &&
                string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.TrackName, trackName, StringComparison.OrdinalIgnoreCase)),
            plays.Count(c =>
                c.TimePlayed >= monthAgo &&
                string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.TrackName, trackName, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<int> GetWeekAlbumPlaycountAsync(int userId, string albumName, string artistName)
    {
        var plays = await GetWeekPlays(userId);
        return plays.Count(ab => ab.AlbumName != null &&
                                 string.Equals(ab.AlbumName, albumName, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(ab.ArtistName, artistName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(int week, int month)> GetRecentAlbumPlaycounts(int userId, string albumName, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var monthAgo = DateTime.UtcNow.AddMonths(-1);

        var importUser = await UserRepository.GetImportUserForUserId(userId, connection);
        var plays = await PlayRepository.GetUserPlays(userId, connection, importUser?.DataSource ?? DataSource.LastFm,
            start: monthAgo);

        return (
            plays.Count(c =>
                c.TimePlayed >= weekAgo &&
                string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.AlbumName, albumName, StringComparison.OrdinalIgnoreCase)),
            plays.Count(c =>
                c.TimePlayed >= monthAgo &&
                string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.AlbumName, albumName, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<int> GetArtistPlaycountForTimePeriodAsync(int userId, string artistName, int daysToGoBack = 7)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var start = DateTime.UtcNow.AddDays(-daysToGoBack);
        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(userId, connection, start);

        return plays.Count(a => string.Equals(a.ArtistName, artistName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(int week, int month)> GetRecentArtistPlaycounts(int userId, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var monthAgo = DateTime.UtcNow.AddMonths(-1);

        var importUser = await UserRepository.GetImportUserForUserId(userId, connection);
        var plays = await PlayRepository.GetUserPlays(userId, connection, importUser?.DataSource ?? DataSource.LastFm,
            start: monthAgo);

        return (
            plays.Count(c =>
                c.TimePlayed >= weekAgo && string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase)),
            plays.Count(c =>
                c.TimePlayed >= monthAgo &&
                string.Equals(c.ArtistName, artistName, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<List<UserStreak>> GetStreaks(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserStreaks
            .Where(w => w.UserId == userId)
            .Where(w => w.ArtistName != null || w.AlbumName != null || w.TrackName != null || w.GenreStreaks != null)
            .OrderByDescending(o => o.ArtistPlaycount.HasValue)
            .ThenByDescending(o => o.ArtistPlaycount)
            .ToListAsync();
    }

    public async Task DeleteStreak(long streakId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var streak = await db.UserStreaks
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserStreakId == streakId);

        db.UserStreaks.Remove(streak);
        await db.SaveChangesAsync();
    }

    public async Task<int> DeleteAllStreaks(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserStreaks
            .Where(w => w.UserId == userId)
            .ExecuteDeleteAsync();
    }

    public static UserStreak GetCurrentStreak(int userId, RecentTrack lastPlay,
        ICollection<UserPlay> lastPlays)
    {
        if (!lastPlays.Any() || lastPlay == null)
        {
            return null;
        }

        var lastPlaysList = lastPlays
            .OrderByDescending(o => o.TimePlayed)
            .Where(w => !lastPlay.TimePlayed.HasValue || w.TimePlayed < lastPlay.TimePlayed.Value)
            .ToList();

        var streak = new UserStreak
        {
            ArtistPlaycount = 1,
            AlbumPlaycount = 1,
            TrackPlaycount = 1,
            StreakEnded = lastPlay.TimePlayed ?? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            StreakStarted = lastPlay.TimePlayed ?? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            UserId = userId
        };

        var startIndex = lastPlay.NowPlaying ? 1 : 0;
        for (var i = startIndex; i < lastPlaysList.Count; i++)
        {
            var currentPlay = lastPlaysList[i];

            if (lastPlay.ArtistName.ToLower() == currentPlay.ArtistName.ToLower())
            {
                streak.ArtistPlaycount++;
                streak.ArtistName = currentPlay.ArtistName;
                if (currentPlay.TimePlayed < streak.StreakStarted)
                {
                    streak.StreakStarted = currentPlay.TimePlayed;
                }
            }
            else
            {
                break;
            }
        }

        for (var i = startIndex; i < lastPlaysList.Count; i++)
        {
            var currentPlay = lastPlaysList[i];

            if (lastPlay.AlbumName != null &&
                currentPlay.AlbumName != null &&
                lastPlay.AlbumName.ToLower() == currentPlay.AlbumName.ToLower())
            {
                streak.AlbumPlaycount++;
                streak.AlbumName = currentPlay.AlbumName;
                if (currentPlay.TimePlayed < streak.StreakStarted)
                {
                    streak.StreakStarted = currentPlay.TimePlayed;
                }
            }
            else
            {
                break;
            }
        }

        for (var i = startIndex; i < lastPlaysList.Count; i++)
        {
            var currentPlay = lastPlaysList[i];

            if (string.Equals(lastPlay.TrackName.ToLower(), currentPlay.TrackName.ToLower(), StringComparison.Ordinal) &&
                lastPlay.ArtistName.ToLower() == currentPlay.ArtistName.ToLower())
            {
                streak.TrackPlaycount++;
                streak.TrackName = currentPlay.TrackName;
                if (currentPlay.TimePlayed < streak.StreakStarted)
                {
                    streak.StreakStarted = currentPlay.TimePlayed;
                }
            }
            else
            {
                break;
            }
        }

        return streak;
    }

    private const int GenreStreakChunkSize = 1000;

    public static List<GenreStreakCandidate> SeedGenreStreakCandidates(List<string> seedGenres, DateTime streakStarted)
    {
        if (seedGenres == null || seedGenres.Count == 0)
        {
            return [];
        }

        return seedGenres
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => new GenreStreakCandidate
            {
                GenreName = s,
                Playcount = 1,
                Alive = true,
                StreakStarted = streakStarted
            })
            .ToList();
    }

    public static bool WalkGenreStreak(IReadOnlyList<UserPlay> plays, List<GenreStreakCandidate> candidates,
        IReadOnlyDictionary<int, List<string>> genreMap)
    {
        if (candidates.Count == 0)
        {
            return false;
        }

        foreach (var play in plays)
        {
            HashSet<string> playGenres = null;
            if (play.ArtistId.HasValue && genreMap.TryGetValue(play.ArtistId.Value, out var genres))
            {
                playGenres = new HashSet<string>(genres, StringComparer.OrdinalIgnoreCase);
            }

            var anyAlive = false;
            foreach (var candidate in candidates)
            {
                if (!candidate.Alive)
                {
                    continue;
                }

                if (playGenres != null && playGenres.Contains(candidate.GenreName))
                {
                    candidate.Playcount++;
                    if (play.TimePlayed < candidate.StreakStarted)
                    {
                        candidate.StreakStarted = play.TimePlayed;
                    }

                    anyAlive = true;
                }
                else
                {
                    candidate.Alive = false;
                }
            }

            if (!anyAlive)
            {
                return false;
            }
        }

        return true;
    }

    public async Task ApplyGenreStreaks(UserStreak streak, RecentTrack lastPlay, ICollection<UserPlay> lastPlays)
    {
        if (streak == null || lastPlay?.ArtistName == null || lastPlays.Count == 0)
        {
            return;
        }

        var seedGenres = await this._genreService.GetGenresForArtist(lastPlay.ArtistName);
        var candidates = SeedGenreStreakCandidates(seedGenres,
            lastPlay.TimePlayed ?? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));

        if (candidates.Count == 0)
        {
            return;
        }

        var lastPlaysList = lastPlays
            .OrderByDescending(o => o.TimePlayed)
            .Where(w => !lastPlay.TimePlayed.HasValue || w.TimePlayed < lastPlay.TimePlayed.Value)
            .ToList();

        var startIndex = lastPlay.NowPlaying ? 1 : 0;
        var genreMap = new Dictionary<int, List<string>>();

        for (var i = startIndex; i < lastPlaysList.Count; i += GenreStreakChunkSize)
        {
            var chunk = lastPlaysList.GetRange(i, Math.Min(GenreStreakChunkSize, lastPlaysList.Count - i));

            var newArtistIds = chunk
                .Where(w => w.ArtistId.HasValue && !genreMap.ContainsKey(w.ArtistId.Value))
                .Select(s => s.ArtistId.Value)
                .Distinct()
                .ToList();

            if (newArtistIds.Count != 0)
            {
                var newGenres = await this._genreService.GetGenresByArtistIds(newArtistIds);
                foreach (var (artistId, artistGenres) in newGenres)
                {
                    genreMap[artistId] = artistGenres;
                }
            }

            if (!WalkGenreStreak(chunk, candidates, genreMap))
            {
                break;
            }
        }

        var qualifying = candidates
            .Where(w => w.Playcount >= 2)
            .OrderByDescending(o => o.Playcount)
            .ToList();

        if (qualifying.Count == 0)
        {
            return;
        }

        streak.GenreStreaks = qualifying
            .Select(s => new UserGenreStreak
            {
                GenreName = s.GenreName,
                Playcount = s.Playcount
            })
            .ToList();

        var earliestStart = qualifying.Min(m => m.StreakStarted);
        if (earliestStart < streak.StreakStarted)
        {
            streak.StreakStarted = earliestStart;
        }
    }

    public static bool StreakExists(UserStreak streak)
    {
        if (streak.ArtistName == null &&
            streak.AlbumName == null &&
            streak.TrackName == null &&
            (streak.GenreStreaks == null || streak.GenreStreaks.Count == 0))
        {
            return false;
        }

        return true;
    }

    public static bool ShouldSaveStreak(UserStreak streak)
    {
        if (!StreakExists(streak))
        {
            return false;
        }

        if (streak.GenreStreaks?.Any(a => a.Playcount >= Constants.StreakSaveThreshold) == true)
        {
            return true;
        }

        if (streak.ArtistPlaycount is < Constants.StreakSaveThreshold &&
            streak.AlbumPlaycount is < Constants.StreakSaveThreshold &&
            streak.TrackPlaycount is < Constants.StreakSaveThreshold)
        {
            return false;
        }

        return true;
    }

    public static string StreakToText(UserStreak streak, NumberFormat numberFormat, bool includeStart = true)
    {
        var description = new StringBuilder();

        var musicStreaks = MusicStreaksToText(streak, numberFormat);
        if (musicStreaks != null)
        {
            description.Append(musicStreaks);
        }

        var genreStreaks = GenreStreaksToText(streak, numberFormat);
        if (genreStreaks != null)
        {
            description.Append(genreStreaks);
        }

        if (description.Length == 0)
        {
            return "No active streak found.";
        }

        if (includeStart)
        {
            description.AppendLine();
            description.AppendLine(StreakStartedToText(streak));
        }

        return description.ToString();
    }

    public static string MusicStreaksToText(UserStreak streak, NumberFormat numberFormat)
    {
        var description = new StringBuilder();
        if (streak.ArtistName != null && streak.ArtistPlaycount.HasValue)
        {
            var artistDisplay = HttpUtility.UrlEncode(streak.ArtistName).Length > 80
                ? $"**{streak.ArtistName}**"
                : $"**[{streak.ArtistName}]({LastfmUrlExtensions.GetArtistUrl(streak.ArtistName)})**";

            description.AppendLine(
                $"`Artist:` {artistDisplay} - " +
                $"{GetEmojiForStreakCount(streak.ArtistPlaycount.Value)} {streak.ArtistPlaycount.Format(numberFormat)} {StringExtensions.GetPlaysString(streak.ArtistPlaycount)}");
        }

        if (streak.AlbumName != null && streak.AlbumPlaycount.HasValue)
        {
            var albumDisplay = HttpUtility.UrlEncode(streak.AlbumName).Length + HttpUtility.UrlEncode(streak.ArtistName ?? "").Length > 100
                ? $"**{streak.AlbumName}**"
                : $"**[{streak.AlbumName}](https://www.last.fm/music/{HttpUtility.UrlEncode(streak.ArtistName)}/{HttpUtility.UrlEncode(streak.AlbumName)})**";

            description.AppendLine(
                $"` Album:` {albumDisplay} - " +
                $"{GetEmojiForStreakCount(streak.AlbumPlaycount.Value)} {streak.AlbumPlaycount.Format(numberFormat)} {StringExtensions.GetPlaysString(streak.AlbumPlaycount)}");
        }

        if (streak.TrackName != null && streak.TrackPlaycount.HasValue)
        {
            var trackDisplay = HttpUtility.UrlEncode(streak.TrackName).Length + HttpUtility.UrlEncode(streak.ArtistName ?? "").Length > 100
                ? $"**{streak.TrackName}**"
                : $"**[{streak.TrackName}](https://www.last.fm/music/{HttpUtility.UrlEncode(streak.ArtistName)}/_/{HttpUtility.UrlEncode(streak.TrackName)})**";

            description.AppendLine(
                $"` Track:` {trackDisplay} - " +
                $"{GetEmojiForStreakCount(streak.TrackPlaycount.Value)} {streak.TrackPlaycount.Format(numberFormat)} {StringExtensions.GetPlaysString(streak.TrackPlaycount)}");
        }

        return description.Length > 0 ? description.ToString() : null;
    }

    public static string GenreStreaksToText(UserStreak streak, NumberFormat numberFormat)
    {
        if (streak.GenreStreaks == null || streak.GenreStreaks.Count == 0)
        {
            return null;
        }

        var description = new StringBuilder();
        foreach (var genreStreak in streak.GenreStreaks.OrderByDescending(o => o.Playcount).Take(3))
        {
            description.AppendLine(
                $"` Genre:` **{genreStreak.GenreName.Transform(To.TitleCase)}** - " +
                $"{GetEmojiForStreakCount(genreStreak.Playcount)} {genreStreak.Playcount.Format(numberFormat)} {StringExtensions.GetPlaysString(genreStreak.Playcount)}");
        }

        return description.ToString();
    }

    public static string StreakStartedToText(UserStreak streak)
    {
        var specifiedDateTime = DateTime.SpecifyKind(streak.StreakStarted, DateTimeKind.Utc);
        var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

        return $"Streak started <t:{dateValue}:R>.";
    }

    public async Task<string> UpdateOrInsertStreak(UserStreak currentStreak)
    {
        if (!ShouldSaveStreak(currentStreak))
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingStreak = await db.UserStreaks.FirstOrDefaultAsync(f =>
            f.UserId == currentStreak.UserId &&
            f.StreakStarted == currentStreak.StreakStarted &&
            (f.ArtistName != null || f.AlbumName != null || f.TrackName != null || f.GenreStreaks != null));

        if (existingStreak == null)
        {
            await db.UserStreaks.AddAsync(currentStreak);
            await db.SaveChangesAsync();
            return "Streak has been saved!";
        }

        existingStreak.ArtistName = currentStreak.ArtistName;
        existingStreak.ArtistPlaycount = currentStreak.ArtistPlaycount;
        existingStreak.AlbumName = currentStreak.AlbumName;
        existingStreak.AlbumPlaycount = currentStreak.AlbumPlaycount;
        existingStreak.TrackName = currentStreak.TrackName;
        existingStreak.TrackPlaycount = currentStreak.TrackPlaycount;
        existingStreak.GenreStreaks = currentStreak.GenreStreaks;
        existingStreak.StreakEnded = currentStreak.StreakEnded;

        db.Entry(existingStreak).State = EntityState.Modified;
        await db.SaveChangesAsync();

        return "Saved streak has been updated!";
    }

    public static List<UserStreak> GetHistoricalStreaks(int userId, ICollection<UserPlay> plays,
        IReadOnlyDictionary<int, List<string>> genreMap = null)
    {
        var orderedPlays = plays
            .Where(w => w.ArtistName != null && w.TrackName != null)
            .OrderBy(o => o.TimePlayed)
            .ToList();

        if (orderedPlays.Count == 0)
        {
            return [];
        }

        var streaks = new Dictionary<DateTime, UserStreak>();

        WalkRuns(p => p.ArtistName, (streak, firstPlay, count) =>
        {
            streak.ArtistName = firstPlay.ArtistName;
            streak.ArtistPlaycount = count;
        });

        WalkRuns(p => p.AlbumName, (streak, firstPlay, count) =>
        {
            streak.AlbumName = firstPlay.AlbumName;
            streak.AlbumPlaycount = count;
            streak.ArtistName ??= firstPlay.ArtistName;
        });

        WalkRuns(p => $"{p.ArtistName}\n{p.TrackName}", (streak, firstPlay, count) =>
        {
            streak.TrackName = firstPlay.TrackName;
            streak.TrackPlaycount = count;
            streak.ArtistName ??= firstPlay.ArtistName;
        });

        if (genreMap != null && genreMap.Count != 0)
        {
            WalkGenreRuns();
        }

        return streaks.Values.OrderBy(o => o.StreakStarted).ToList();

        void WalkGenreRuns()
        {
            var activeRuns =
                new Dictionary<string, (int Count, UserPlay FirstPlay, DateTime End)>(StringComparer
                    .OrdinalIgnoreCase);

            foreach (var play in orderedPlays)
            {
                HashSet<string> playGenres = null;
                if (play.ArtistId.HasValue && genreMap.TryGetValue(play.ArtistId.Value, out var genres))
                {
                    playGenres = new HashSet<string>(genres, StringComparer.OrdinalIgnoreCase);
                }

                if (activeRuns.Count != 0)
                {
                    List<string> endedRuns = null;
                    foreach (var genre in activeRuns.Keys)
                    {
                        if (playGenres == null || !playGenres.Contains(genre))
                        {
                            (endedRuns ??= []).Add(genre);
                        }
                    }

                    if (endedRuns != null)
                    {
                        foreach (var genre in endedRuns)
                        {
                            FinalizeGenreRun(genre, activeRuns[genre]);
                            activeRuns.Remove(genre);
                        }
                    }
                }

                if (playGenres == null)
                {
                    continue;
                }

                foreach (var genre in playGenres)
                {
                    activeRuns[genre] = activeRuns.TryGetValue(genre, out var run)
                        ? (run.Count + 1, run.FirstPlay, play.TimePlayed)
                        : (1, play, play.TimePlayed);
                }
            }

            foreach (var (genre, run) in activeRuns)
            {
                FinalizeGenreRun(genre, run);
            }

            return;

            void FinalizeGenreRun(string genre, (int Count, UserPlay FirstPlay, DateTime End) run)
            {
                if (run.Count < Constants.StreakSaveThreshold)
                {
                    return;
                }

                if (!streaks.TryGetValue(run.FirstPlay.TimePlayed, out var streak))
                {
                    streak = new UserStreak
                    {
                        UserId = userId,
                        StreakStarted = run.FirstPlay.TimePlayed,
                        StreakEnded = run.End
                    };
                    streaks.Add(run.FirstPlay.TimePlayed, streak);
                }

                if (run.End > streak.StreakEnded)
                {
                    streak.StreakEnded = run.End;
                }

                streak.GenreStreaks ??= [];
                streak.GenreStreaks.Add(new UserGenreStreak { GenreName = genre, Playcount = run.Count });
            }
        }

        void WalkRuns(Func<UserPlay, string> keySelector, Action<UserStreak, UserPlay, int> applyRun)
        {
            string currentKey = null;
            UserPlay runFirstPlay = null;
            var count = 0;
            var runEnd = DateTime.MinValue;

            foreach (var play in orderedPlays)
            {
                var key = keySelector(play);
                if (key != null && currentKey != null &&
                    key.Equals(currentKey, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    runEnd = play.TimePlayed;
                    continue;
                }

                FinalizeRun();
                currentKey = key;
                runFirstPlay = key != null ? play : null;
                count = 1;
                runEnd = play.TimePlayed;
            }

            FinalizeRun();
            return;

            void FinalizeRun()
            {
                if (runFirstPlay == null || count < Constants.StreakSaveThreshold)
                {
                    return;
                }

                if (!streaks.TryGetValue(runFirstPlay.TimePlayed, out var streak))
                {
                    streak = new UserStreak
                    {
                        UserId = userId,
                        StreakStarted = runFirstPlay.TimePlayed,
                        StreakEnded = runEnd
                    };
                    streaks.Add(runFirstPlay.TimePlayed, streak);
                }

                if (runEnd > streak.StreakEnded)
                {
                    streak.StreakEnded = runEnd;
                }

                applyRun(streak, runFirstPlay, count);
            }
        }
    }

    public async Task<int?> RestoreStreakHistory(int userId)
    {
        var concurrencyCacheKey = $"streak-restore-{userId}";
        if (this._cache.TryGetValue(concurrencyCacheKey, out bool _))
        {
            return null;
        }

        this._cache.Set(concurrencyCacheKey, true, TimeSpan.FromMinutes(10));

        try
        {
            var plays = await this.GetAllUserPlays(userId);
            var artistIds = plays
                .Where(w => w.ArtistId.HasValue)
                .Select(s => s.ArtistId.Value);
            var genreMap = await this._genreService.GetGenresByArtistIds(artistIds);

            var historicalStreaks = GetHistoricalStreaks(userId, plays, genreMap);

            if (historicalStreaks.Count == 0)
            {
                return 0;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingStreaks = await db.UserStreaks
                .Where(w => w.UserId == userId)
                .ToListAsync();

            var missingStreaks = historicalStreaks
                .Where(c => !existingStreaks.Any(e =>
                    e.StreakStarted <= c.StreakEnded && e.StreakEnded >= c.StreakStarted))
                .ToList();

            if (missingStreaks.Count == 0)
            {
                return 0;
            }

            await db.UserStreaks.AddRangeAsync(missingStreaks);
            await db.SaveChangesAsync();

            return missingStreaks.Count;
        }
        finally
        {
            this._cache.Remove(concurrencyCacheKey);
        }
    }

    public static string GetEmojiForStreakCount(int count)
    {
        return count switch
        {
            > 25000 => "🌌 ",
            > 15000 => "🌠 ",
            > 10000 => "🪐 ",
            > 7500 => "🌚 ",
            > 5000 => "🚀 ",
            > 2500 => "😵 ",
            1337 => "🦹‍ ",
            1234 => "🔢 ",
            > 1000 => "😲 ",
            666 => "‍😈 ",
            420 => "🍃 ",
            100 => "💯 ",
            69 => "😎 ",
            > 50 => "🔥 ",
            _ => null
        };
    }

    public async Task<TopArtistList> GetUserTopArtists(int userId, int days)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var start = DateTime.UtcNow.AddDays(-days);
        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(userId, connection, start);

        var topArtists = plays
            .GroupBy(x => x.ArtistName)
            .Select(s => new TopArtist
            {
                ArtistName = s.Key,
                UserPlaycount = s.Count()
            })
            .OrderByDescending(o => o.UserPlaycount);

        return new TopArtistList
        {
            TotalAmount = topArtists.Count(),
            TopArtists = topArtists.ToList()
        };
    }

    public async Task<List<UserTrack>> GetUserTopTracksForArtist(int userId, int days, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var start = DateTime.UtcNow.AddDays(-days);
        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(userId, connection, start);

        return plays.Where(w => w.ArtistName.ToLower() == artistName.ToLower())
            .GroupBy(x => new { x.ArtistName, x.TrackName })
            .Select(s => new UserTrack
            {
                ArtistName = s.Key.ArtistName,
                Name = s.Key.TrackName,
                Playcount = s.Count()
            })
            .OrderByDescending(o => o.Playcount)
            .ToList();
    }

    public async Task<List<WhoKnowsObjectWithUser>> GetGuildUsersTotalPlaycount(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers,
        int guildId)
    {
        const string sql = "SELECT u.total_playcount AS playcount, " +
                           "u.user_id " +
                           "FROM users AS u " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                           "WHERE gu.guild_id = @guildId AND u.total_playcount is not null " +
                           "ORDER BY u.total_playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userPlaycounts = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            guildId,
        })).ToList();

        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        for (var i = 0; i < userPlaycounts.Count; i++)
        {
            var userAlbum = userPlaycounts[i];

            if (!guildUsers.TryGetValue(userAlbum.UserId, out var guildUser))
            {
                continue;
            }

            var userName = guildUser.UserName ?? guildUser.UserNameLastFM;

            if (i <= 10)
            {
                if (discordGuild.Users.TryGetValue(guildUser.DiscordUserId, out var discordUser))
                {
                    userName = discordUser.GetDisplayName();
                }
            }

            whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userAlbum.Playcount,
                LastFMUsername = guildUser.UserNameLastFM,
                UserId = userAlbum.UserId,
                LastUsed = guildUser.LastUsed,
                LastMessage = guildUser.LastMessage,
                Roles = guildUser.Roles
            });
        }

        return whoKnowsAlbumList;
    }

    public async Task<int> GetWeekArtistPlaycountForGuildAsync(int guildId, string artistName)
    {
        var minDate = DateTime.UtcNow.AddDays(-7);

        const string sql = "SELECT coalesce(count(up.time_played), 0) " +
                           "FROM user_plays AS up " +
                           "INNER JOIN users AS u ON up.user_id = u.user_id " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                           "WHERE gu.guild_id = @guildId AND " +
                           "UPPER(up.artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                           "up.time_played >= @minDate";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await connection.QuerySingleAsync<int>(sql, new
        {
            guildId,
            artistName,
            minDate
        });
    }

    public async Task<UserPlay> GetArtistFirstPlay(int userId, string artistName)
    {
        var plays = await this.GetAllUserPlays(userId);
        return plays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DateTime?> GetArtistFirstPlayDate(int userId, string artistName)
    {
        var plays = await this.GetAllUserPlays(userId);
        return plays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    public async Task<DateTime?> GetAlbumFirstPlayDate(int userId, string artistName, string albumName)
    {
        if (albumName == null)
        {
            return null;
        }

        var plays = await this.GetAllUserPlays(userId);
        return plays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                f.AlbumName != null &&
                f.AlbumName.Equals(albumName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    public async Task<DateTime?> GetTrackFirstPlayDate(int userId, string artistName, string trackName)
    {
        var plays = await this.GetAllUserPlays(userId);
        return plays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                f.TrackName.Equals(trackName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    private static readonly TimeSpan LastListenedExclusionWindow = TimeSpan.FromMinutes(30);

    private static DateTime? GetLastListenedCutoff(ICollection<UserPlay> plays)
    {
        if (plays.Count == 0)
        {
            return null;
        }

        return plays.Max(p => p.TimePlayed) - LastListenedExclusionWindow;
    }

    public async Task<UserPlay> GetArtistLastPlay(int userId, string artistName)
    {
        var plays = await this.GetAllUserPlays(userId);
        var cutoff = GetLastListenedCutoff(plays);
        if (cutoff == null)
        {
            return null;
        }

        return plays
            .Where(w => w.TimePlayed < cutoff.Value)
            .OrderByDescending(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DateTime?> GetArtistLastPlayDate(int userId, string artistName)
    {
        var plays = await this.GetAllUserPlays(userId);
        var cutoff = GetLastListenedCutoff(plays);
        if (cutoff == null)
        {
            return null;
        }

        return plays
            .Where(w => w.TimePlayed < cutoff.Value)
            .OrderByDescending(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    public async Task<DateTime?> GetAlbumLastPlayDate(int userId, string artistName, string albumName)
    {
        if (albumName == null)
        {
            return null;
        }

        var plays = await this.GetAllUserPlays(userId);
        var cutoff = GetLastListenedCutoff(plays);
        if (cutoff == null)
        {
            return null;
        }

        return plays
            .Where(w => w.TimePlayed < cutoff.Value)
            .OrderByDescending(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                f.AlbumName != null &&
                f.AlbumName.Equals(albumName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    public async Task<DateTime?> GetTrackLastPlayDate(int userId, string artistName, string trackName)
    {
        var plays = await this.GetAllUserPlays(userId);
        var cutoff = GetLastListenedCutoff(plays);
        if (cutoff == null)
        {
            return null;
        }

        return plays
            .Where(w => w.TimePlayed < cutoff.Value)
            .OrderByDescending(o => o.TimePlayed)
            .FirstOrDefault(f =>
                f.ArtistName != null &&
                f.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                f.TrackName.Equals(trackName, StringComparison.OrdinalIgnoreCase))?
            .TimePlayed;
    }

    public async Task<IList<UserPlay>> GetGuildUsersPlays(int guildId, int amountOfDays)
    {
        var cacheKey = $"guild-user-plays-{guildId}-{amountOfDays}";

        var cachedPlaysAvailable = this._cache.TryGetValue(cacheKey, out List<UserPlay> userPlays);
        if (cachedPlaysAvailable)
        {
            return userPlays;
        }

        var sql = "SELECT up.* " +
                  "FROM user_plays AS up " +
                  "INNER JOIN users AS u ON up.user_id = u.user_id  " +
                  "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                  $"WHERE gu.guild_id = @guildId  AND gu.bot != true AND time_played > current_date - interval '{amountOfDays}' day AND artist_name IS NOT NULL " +
                  "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        userPlays = (await connection.QueryAsync<UserPlay>(sql, new
        {
            guildId
        })).ToList();

        this._cache.Set(cacheKey, userPlays, TimeSpan.FromMinutes(10));

        return userPlays;
    }

    public async Task<List<GuildTrack>> GetGuildTopTracksPlays(int guildId, DateTime startDateTime,
        OrderType orderType, string searchValue, DateTime? endDateTime = null, int limit = 120,
        int[] userIds = null)
    {
        var cacheKey = $"guild-top-tracks-{guildId}-{startDateTime:yyyyMMddHH}-{endDateTime:yyyyMMddHH}-{orderType}-{searchValue}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out List<GuildTrack> cachedTracks))
        {
            return cachedTracks;
        }

        var artistFilter = !string.IsNullOrWhiteSpace(searchValue)
            ? "AND UPPER(up.artist_name) = UPPER(CAST(@searchValue AS CITEXT)) "
            : "";

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT t.name AS TrackName, " +
                  "t.artist_name AS ArtistName, " +
                  "agg.track_id AS TrackId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.track_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.track_id IS NOT NULL " +
                  $"      {artistFilter}" +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.track_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN tracks t ON t.id = agg.track_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var tracks = (await connection.QueryAsync<GuildTrack>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            searchValue,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();

        if (userIds == null)
        {
            this._cache.Set(cacheKey, tracks, TimeSpan.FromMinutes(10));
        }

        return tracks;
    }

    public async Task<List<GuildArtist>> GetGuildTopArtistsPlays(int guildId, DateTime startDateTime,
        OrderType orderType, DateTime? endDateTime = null, int limit = 120, int[] userIds = null)
    {
        var cacheKey = $"guild-top-artists-{guildId}-{startDateTime:yyyyMMddHH}-{endDateTime:yyyyMMddHH}-{orderType}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out List<GuildArtist> cachedArtists))
        {
            return cachedArtists;
        }

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT a.name AS ArtistName, " +
                  "agg.artist_id AS ArtistId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.artist_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.artist_id IS NOT NULL " +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.artist_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN artists a ON a.id = agg.artist_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artists = (await connection.QueryAsync<GuildArtist>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();

        if (userIds == null)
        {
            this._cache.Set(cacheKey, artists, TimeSpan.FromMinutes(10));
        }

        return artists;
    }

    public async Task<List<GuildAlbum>> GetGuildTopAlbumsPlays(int guildId, DateTime startDateTime,
        OrderType orderType, string searchValue, DateTime? endDateTime = null, int limit = 120,
        int[] userIds = null)
    {
        var cacheKey = $"guild-top-albums-{guildId}-{startDateTime:yyyyMMddHH}-{endDateTime:yyyyMMddHH}-{orderType}-{searchValue}-{limit}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out List<GuildAlbum> cachedAlbums))
        {
            return cachedAlbums;
        }

        var artistFilter = !string.IsNullOrWhiteSpace(searchValue)
            ? "AND UPPER(up.artist_name) = UPPER(CAST(@searchValue AS CITEXT)) "
            : "";

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT al.name AS AlbumName, " +
                  "al.artist_name AS ArtistName, " +
                  "agg.album_id AS AlbumId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.album_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.album_id IS NOT NULL " +
                  $"      {artistFilter}" +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.album_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN albums al ON al.id = agg.album_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var albums = (await connection.QueryAsync<GuildAlbum>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            searchValue,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();

        if (userIds == null)
        {
            this._cache.Set(cacheKey, albums, TimeSpan.FromMinutes(10));
        }

        return albums;
    }

    public async Task<GuildPlayStats> GetGuildPlayStats(int guildId, DateTime startDateTime, DateTime endDateTime,
        int[] userIds = null)
    {
        var cacheKey = $"guild-play-stats-{guildId}-{startDateTime:yyyyMMddHH}-{endDateTime:yyyyMMddHH}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out GuildPlayStats cachedStats))
        {
            return cachedStats;
        }

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var sql = "SELECT COUNT(*)::int AS TotalPlaycount, " +
                  "       COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "FROM user_plays up " +
                  "INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "WHERE gu.guild_id = @guildId " +
                  "  AND gu.bot != true " +
                  "  AND up.time_played > @startDateTime " +
                  "  AND up.time_played < @endDateTime " +
                  $"  {userFilter}" +
                  "  AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "  AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var stats = await connection.QuerySingleAsync<GuildPlayStats>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            userIds
        }, commandTimeout: 300);

        if (userIds == null)
        {
            this._cache.Set(cacheKey, stats, TimeSpan.FromMinutes(10));
        }

        return stats;
    }

    public async Task<List<GuildGenre>> GetGuildTopGenresPlays(int guildId, DateTime startDateTime,
        OrderType orderType, DateTime? endDateTime = null, int limit = 240)
    {
        var cacheKey = $"guild-top-genres-{guildId}-{startDateTime:yyyyMMddHH}-{endDateTime:yyyyMMddHH}-{orderType}";

        if (this._cache.TryGetValue(cacheKey, out List<GuildGenre> cachedGenres))
        {
            return cachedGenres;
        }

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT genre_name AS GenreName, " +
                  "       SUM(user_plays)::bigint AS TotalPlaycount, " +
                  "       COUNT(*)::bigint AS ListenerCount " +
                  "FROM ( " +
                  "    SELECT ag.name AS genre_name, agg.user_id, SUM(agg.plays) AS user_plays " +
                  "    FROM ( " +
                  "        SELECT up.artist_id, up.user_id, COUNT(*) AS plays " +
                  "        FROM user_plays up " +
                  "        INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "        WHERE gu.guild_id = @guildId " +
                  "          AND gu.bot != true " +
                  "          AND up.time_played > @startDateTime " +
                  $"          {endDateFilter}" +
                  "          AND up.artist_id IS NOT NULL " +
                  "          AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "          AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "        GROUP BY up.artist_id, up.user_id " +
                  "    ) agg " +
                  "    INNER JOIN artist_genres ag ON ag.artist_id = agg.artist_id " +
                  "    GROUP BY ag.name, agg.user_id " +
                  ") genre_users " +
                  "GROUP BY genre_name " +
                  $"ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "LIMIT @limit";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var genres = (await connection.QueryAsync<GuildGenre>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            limit
        }, commandTimeout: 300)).ToList();

        this._cache.Set(cacheKey, genres, TimeSpan.FromMinutes(10));

        return genres;
    }

    public async Task<List<UserPlay>> GetGuildUsersPlaysForTimeLeaderBoard(int guildId)
    {
        const string sql =
            "SELECT up.user_play_id, up.user_id, up.track_name, up.album_name, up.artist_name, up.time_played " +
            "FROM public.user_plays AS up " +
            "INNER JOIN users AS u ON up.user_id = u.user_id " +
            "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
            "WHERE gu.guild_id = @guildId " +
            "AND time_played > current_date - interval '9' day  AND time_played < current_date - interval '2' day AND artist_name IS NOT NULL " +
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

    public async Task<bool> UserHasImportedLastFm(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetAllUserPlays(userId, connection);

        if (!plays.Any() || plays.Count < 2000)
        {
            return false;
        }

        return plays
            .Where(w => w.PlaySource == PlaySource.LastFm)
            .GroupBy(g => g.TimePlayed.Date)
            .Count(w => w.Count() > 2500) >= 7;
    }

    public static bool UserHasImported(IEnumerable<UserPlay> userPlays)
    {
        return userPlays
            .Where(w => w.PlaySource == PlaySource.LastFm)
            .GroupBy(g => g.TimePlayed.Date)
            .Count(w => w.Count() > 2500) >= 7;
    }

    public Task<ICollection<UserPlay>> GetAllUserPlays(int userId, bool finalizeImport = true)
    {
        var cacheKey = $"all-user-plays-{userId}-{finalizeImport}";

        if (this._cache.TryGetValue(cacheKey, out Task<ICollection<UserPlay>> existingTask))
        {
            return existingTask;
        }

        var task = GetAllUserPlaysInternal(userId, finalizeImport);
        this._cache.Set(cacheKey, task, TimeSpan.FromSeconds(5));

        _ = task.ContinueWith(_ => this._cache.Remove(cacheKey),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        return task;
    }

    private async Task<ICollection<UserPlay>> GetAllUserPlaysInternal(int userId, bool finalizeImport)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        ICollection<UserPlay> plays;

        if (finalizeImport)
        {
            var importUser = await UserRepository.GetImportUserForUserId(userId, connection, true);
            if (importUser != null)
            {
                plays = await PlayRepository.GetUserPlays(userId, connection, importUser.DataSource);
            }
            else
            {
                plays = await PlayRepository.GetUserPlays(userId, connection, DataSource.LastFm);
            }
        }
        else
        {
            plays = await PlayRepository.GetAllUserPlays(userId, connection);
        }

        return plays;
    }

    public async Task<ICollection<UserPlay>> GetPlaysWithDataSource(int userId, DataSource dataSource)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await PlayRepository.GetUserPlays(userId, connection, dataSource);
    }

    public async Task<RecentTrackList> AddUserPlaysToRecentTracks(int userId, RecentTrackList recentTracks,
        int limit = int.MaxValue)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var importUser = await UserRepository.GetImportUserForUserId(userId, connection, true);
        var plays = await PlayRepository.GetUserPlays(userId, connection, importUser?.DataSource ?? DataSource.LastFm,
            limit);

        var firstRecentTrack = recentTracks.RecentTracks
            .Where(w => w.TimePlayed != null)
            .MinBy(o => o.TimePlayed);

        var playsToAdd = firstRecentTrack == null
            ? Enumerable.Empty<UserPlay>()
            : plays.Where(w => w.TimePlayed < firstRecentTrack.TimePlayed);

        foreach (var play in playsToAdd)
        {
            recentTracks.RecentTracks.Add(UserPlayToRecentTrack(play));
        }

        if (limit == int.MaxValue)
        {
            recentTracks.TotalAmount = recentTracks.RecentTracks.Count;
        }

        recentTracks.RecentTracks = recentTracks.RecentTracks
            .OrderByDescending(o => o.NowPlaying)
            .ThenByDescending(o => o.TimePlayed)
            .ToList();

        return recentTracks;
    }

    public async Task<bool> HasPlayNearTimestamp(int userId, DateTime timestamp, int secondsRange = 30)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await PlayRepository.HasPlayNearTimestamp(userId, connection, timestamp, secondsRange);
    }

    public async Task<List<RecentTrack>> GetCachedPlaysForUser(int userId, int limit = 120)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var importUser = await UserRepository.GetImportUserForUserId(userId, connection);
        var plays = await PlayRepository.GetUserPlays(userId, connection, importUser?.DataSource ?? DataSource.LastFm, limit);

        return plays.Select(UserPlayToRecentTrack).ToList();
    }

    private static RecentTrack UserPlayToRecentTrack(UserPlay userPlay)
    {
        return new RecentTrack
        {
            AlbumName = userPlay.AlbumName,
            AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(userPlay.ArtistName, userPlay.AlbumName),
            ArtistName = userPlay.ArtistName,
            ArtistUrl = LastfmUrlExtensions.GetArtistUrl(userPlay.ArtistName),
            TrackName = userPlay.TrackName,
            TrackUrl = LastfmUrlExtensions.GetTrackUrl(userPlay.ArtistName, userPlay.TrackName),
            TimePlayed = userPlay.TimePlayed,
            PlaySource = userPlay.PlaySource
        };
    }

    public async Task MoveData(int oldUserId, int newUserId, bool moveImports = true)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        if (moveImports)
        {
            await PlayRepository.MoveImports(oldUserId, newUserId, connection);
        }

        await PlayRepository.MoveStreaks(oldUserId, newUserId, connection);
        await PlayRepository.MoveFeaturedLogs(oldUserId, newUserId, connection);
        await PlayRepository.MoveFriends(oldUserId, newUserId, connection);
    }
}
