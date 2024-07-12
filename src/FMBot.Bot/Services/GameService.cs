using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using SkiaSharp;

namespace FMBot.Bot.Services;

public class GameService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly HttpClient _client;

    public const int JumbleSecondsToGuess = 25;
    public const int PixelationSecondsToGuess = 40;

    public GameService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, HttpClient client)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._client = client;
        this._botSettings = botSettings.Value;
    }

    public static (string artist, long userPlaycount) PickArtistForJumble(List<TopArtist> topArtists, List<JumbleSession> recentJumbles = null)
    {
        recentJumbles ??= [];

        var today = DateTime.Today;
        var recentJumblesHashset = recentJumbles
            .Where(w => w.DateStarted.Date == today)
            .GroupBy(g => g.CorrectAnswer)
            .Select(s => s.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (topArtists.Count > 250 && recentJumbles.Count > 50)
        {
            var recentJumbleAnswers = recentJumbles
                .GroupBy(g => g.CorrectAnswer)
                .Select(s => s.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            recentJumblesHashset.UnionWith(recentJumbleAnswers);
        }

        topArtists = topArtists
            .Where(w => !recentJumblesHashset.Contains(w.ArtistName) &&
                        w.ArtistName.Length is > 2 and < 40 &&
                        !w.ArtistName.StartsWith(ConfigData.Data.Bot.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.UserPlaycount)
            .ToList();

        var multiplier = topArtists.Count switch
        {
            > 5000 => 6,
            > 2500 => 4,
            > 1200 => 3,
            > 500 => 2,
            _ => 1
        };

        var minPlaycount = recentJumbles.Count(w => w.DateStarted.Date >= today.AddDays(-4)) switch
        {
            >= 75 => 1,
            >= 40 => 2,
            >= 12 => 5,
            >= 4 => 15,
            _ => 30
        };

        var finalMinPlaycount = minPlaycount * multiplier;
        if (recentJumbles.Count(w => w.DateStarted.Date == today) >= 200)
        {
            finalMinPlaycount = 1;
        }

        var eligibleArtists = topArtists
            .Where(w => w.UserPlaycount >= finalMinPlaycount)
            .ToList();

        Log.Information("PickArtistForJumble: {topArtistCount} top artists - {jumblesPlayedTodayCount} jumbles played today - " +
                        "{multiplier} multiplier - {minPlaycount} min playcount - {finalMinPlaycount} final min playcount",
            topArtists.Count, recentJumbles.Count, multiplier, minPlaycount, finalMinPlaycount);

        if (eligibleArtists.Count == 0)
        {
            TopArtist fallbackArtist = null;
            if (topArtists.Count > 0)
            {
                var fallBackIndex = RandomNumberGenerator.GetInt32(topArtists.Count);
                fallbackArtist = topArtists
                    .Where(w => !recentJumblesHashset.Contains(w.ArtistName))
                    .OrderByDescending(o => o.UserPlaycount)
                    .ElementAtOrDefault(fallBackIndex);
            }

            return fallbackArtist != null ? (fallbackArtist.ArtistName, fallbackArtist.UserPlaycount) : (null, 0);
        }

        var randomIndex = RandomNumberGenerator.GetInt32(eligibleArtists.Count);
        return (eligibleArtists[randomIndex].ArtistName, eligibleArtists[randomIndex].UserPlaycount);
    }

    public static TopAlbum PickAlbumForPixelation(List<TopAlbum> topAlbums, List<JumbleSession> recentJumbles = null)
    {
        recentJumbles ??= [];

        var today = DateTime.Today;
        var recentJumblesHashset = recentJumbles
            .Where(w => w.DateStarted.Date == today)
            .GroupBy(g => g.CorrectAnswer)
            .Select(s => s.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (topAlbums.Count > 250 && recentJumbles.Count > 50)
        {
            var recentJumbleAnswers = recentJumbles
                .GroupBy(g => g.CorrectAnswer)
                .Select(s => s.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            recentJumblesHashset.UnionWith(recentJumbleAnswers);
        }

        topAlbums = topAlbums
            .Where(w => w.AlbumCoverUrl != null &&
                        !recentJumblesHashset.Contains(w.AlbumName) &&
                        w.AlbumName.Length is > 2 and < 50 &&
                        !w.AlbumName.StartsWith(ConfigData.Data.Bot.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.UserPlaycount)
            .ToList();

        var multiplier = topAlbums.Count switch
        {
            > 5000 => 6,
            > 2500 => 4,
            > 1200 => 3,
            > 500 => 2,
            _ => 1
        };

        var minPlaycount = recentJumbles.Count(w => w.DateStarted.Date >= today.AddDays(-4)) switch
        {
            >= 75 => 1,
            >= 40 => 2,
            >= 12 => 5,
            >= 4 => 15,
            _ => 30
        };

        var finalMinPlaycount = minPlaycount * multiplier;
        if (recentJumbles.Count(w => w.DateStarted.Date == today) >= 200)
        {
            finalMinPlaycount = 1;
        }

        var eligibleAlbums = topAlbums
            .Where(w => w.UserPlaycount >= finalMinPlaycount)
            .ToList();

        Log.Information("PickAlbumForPixelation: {topArtistCount} top artists - {jumblesPlayedTodayCount} jumbles played today - " +
                        "{multiplier} multiplier - {minPlaycount} min playcount - {finalMinPlaycount} final min playcount",
            topAlbums.Count, recentJumbles.Count, multiplier, minPlaycount, finalMinPlaycount);

        if (eligibleAlbums.Count == 0)
        {
            TopAlbum fallbackAlbum = null;
            if (topAlbums.Count > 0)
            {
                var fallBackIndex = RandomNumberGenerator.GetInt32(topAlbums.Count);
                fallbackAlbum = topAlbums
                    .Where(w => !recentJumblesHashset.Contains(w.AlbumName))
                    .OrderByDescending(o => o.UserPlaycount)
                    .ElementAtOrDefault(fallBackIndex);
            }

            return fallbackAlbum;
        }

        var randomIndex = RandomNumberGenerator.GetInt32(eligibleAlbums.Count);
        return eligibleAlbums[randomIndex];
    }

    public async Task<JumbleSession> StartJumbleGame(int userId, ContextModel context, JumbleType jumbleType,
                                                     string answer, CancellationTokenSource cancellationToken,
                                                     string artist, string album = null)
    {
        if (jumbleType == JumbleType.Pixelation)
        {
            answer = StringExtensions.RemoveEditionSuffix(answer);
        }

        var jumbleSession = new JumbleSession
        {
            DateStarted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            StarterUserId = userId,
            DiscordGuildId = context.DiscordGuild?.Id,
            DiscordChannelId = context.DiscordChannel.Id,
            DiscordId = context.InteractionId,
            JumbleType = jumbleType,
            CorrectAnswer = answer,
            Reshuffles = 0,
            ArtistName = artist,
            AlbumName = album
        };

        if (jumbleType == JumbleType.Artist)
        {
            jumbleSession.JumbledArtist = JumbleWords(answer).ToUpper();

            if (jumbleSession.JumbledArtist.Equals(answer, StringComparison.OrdinalIgnoreCase))
            {
                jumbleSession.JumbledArtist = JumbleWords(answer).ToUpper();
            }
        }

        if (jumbleType == JumbleType.Pixelation)
        {
            jumbleSession.BlurLevel = 0.10f;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        await db.JumbleSessions.AddAsync(jumbleSession);

        await db.SaveChangesAsync();

        var cacheTime = TimeSpan.FromMinutes(1);
        this._cache.Set(CacheKeyForJumbleSession(context.DiscordChannel.Id), cancellationToken, cacheTime);
        this._cache.Set(CacheKeyForJumbleSessionCancellationToken(context.DiscordChannel.Id), cancellationToken, cacheTime);

        return jumbleSession;
    }

    public static string CacheKeyForJumbleSession(ulong channelId)
    {
        return $"jumble-session-active-{channelId}";
    }

    public static string CacheKeyForJumbleStarting(ulong channelId)
    {
        return $"jumble-session-starting-{channelId}";
    }

    private static string CacheKeyForJumbleSessionCancellationToken(ulong channelId)
    {
        return $"jumble-session-token-{channelId}";
    }

    private static string CacheKeyForJumbleSessionImage(int sessionId)
    {
        return $"jumble-session-image-{sessionId}";
    }

    public async Task<JumbleSession> GetJumbleSessionForSessionId(int jumbleSessionId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.JumbleSessions
            .OrderByDescending(o => o.DateStarted)
            .Include(i => i.Hints)
            .Include(i => i.Answers)
            .FirstOrDefaultAsync(f => f.JumbleSessionId == jumbleSessionId);
    }

    public async Task<JumbleSession> GetJumbleSessionForChannelId(ulong discordChannelId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.JumbleSessions
            .OrderByDescending(o => o.DateStarted)
            .Include(i => i.Hints)
            .Include(i => i.Answers)
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);
    }

    public void GameStartInProgress(ulong discordChannelId)
    {
        this._cache.Set(CacheKeyForJumbleStarting(discordChannelId), true, TimeSpan.FromSeconds(3));
    }

    public bool GameStartingAlready(ulong discordChannelId)
    {
        return this._cache.TryGetValue(CacheKeyForJumbleStarting(discordChannelId), out _);
    }

    public async Task<List<JumbleSession>> GetRecentJumbles(int userId, JumbleType jumbleType)
    {
        const string sql = "SELECT correct_answer, date_started FROM public.jumble_sessions " +
                           "WHERE starter_user_id = @userId AND jumble_type = @jumbleType LIMIT 250 ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var jumblesPlayedToday = await connection.QueryAsync<JumbleSession>(sql, new
        {
            userId,
            jumbleType
        });

        return jumblesPlayedToday.ToList();
    }

    public async Task CancelToken(ulong channelId)
    {
        if (!this._cache.TryGetValue(CacheKeyForJumbleSessionCancellationToken(channelId), out CancellationTokenSource token))
        {
            return;
        }

        await token.CancelAsync();
    }

    public static List<JumbleSessionHint> GetJumbleArtistHints(Artist artist, long userPlaycount, CountryInfo country = null)
    {
        var hints = GetRandomArtistHints(artist, country);
        hints.Add(new JumbleSessionHint(JumbleHintType.Playcount, $"- You have **{userPlaycount}** {StringExtensions.GetPlaysString(userPlaycount)} on this artist"));

        RandomNumberGenerator.Shuffle(CollectionsMarshal.AsSpan(hints));

        for (int i = 0; i < Math.Min(hints.Count, 3); i++)
        {
            hints[i].HintShown = true;
            hints[i].Order = i;
        }

        return hints;
    }

    public static List<JumbleSessionHint> GetJumbleAlbumHints(Album album, Artist artist, long userPlaycount, CountryInfo country = null)
    {
        var hints = GetRandomAlbumHints(album, artist, country);
        hints.Add(new JumbleSessionHint(JumbleHintType.Playcount, $"- You have **{userPlaycount}** {StringExtensions.GetPlaysString(userPlaycount)} on this album"));

        RandomNumberGenerator.Shuffle(CollectionsMarshal.AsSpan(hints));

        for (int i = 0; i < Math.Min(hints.Count, 3); i++)
        {
            hints[i].HintShown = true;
            hints[i].Order = i;
        }

        return hints;
    }

    public static string HintsToString(List<JumbleSessionHint> hints, int count = 3)
    {
        var hintDescription = new StringBuilder();

        for (int i = 0; i < Math.Min(hints.Count, count); i++)
        {
            hintDescription.AppendLine(hints[i].Content);

            hints[i].HintShown = true;
            hints[i].Order = i;
        }

        return hintDescription.ToString();
    }

    public async Task JumbleStoreShowedHints(JumbleSession game, List<JumbleSessionHint> hints)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        game.Hints = hints;

        db.Update(game);
        await db.SaveChangesAsync();
    }
    
    public async Task JumbleStoreBlurLevel(JumbleSession game, float blurLevel)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        game.BlurLevel = blurLevel;

        db.Update(game);
        await db.SaveChangesAsync();
    }

    public async Task JumbleReshuffleArtist(JumbleSession game)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        game.JumbledArtist = JumbleWords(game.CorrectAnswer).ToUpper();

        if (game.JumbledArtist.Equals(game.CorrectAnswer, StringComparison.OrdinalIgnoreCase))
        {
            game.JumbledArtist = JumbleWords(game.JumbledArtist).ToUpper();
        }

        game.Reshuffles++;

        db.Update(game);
        await db.SaveChangesAsync();
    }

    public async Task JumbleEndSession(JumbleSession game)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        game.DateEnded = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        db.Update(game);
        await db.SaveChangesAsync();
    }

    public async Task JumbleAddResponseId(int gameSessionId, ulong responseId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var game = await GetJumbleSessionForSessionId(gameSessionId);

        game.DiscordResponseId = responseId;

        db.Update(game);
        await db.SaveChangesAsync();
    }

    public async Task JumbleAddAnswer(JumbleSession game, ulong discordUserId, bool correct)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var answer = new JumbleSessionAnswer
        {
            Correct = correct,
            DiscordUserId = discordUserId,
            JumbleSessionId = game.JumbleSessionId,
            DateAnswered = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        };

        await db.JumbleSessionAnswers.AddAsync(answer);

        await db.SaveChangesAsync();
    }

    private static List<JumbleSessionHint> GetRandomArtistHints(Artist artist, CountryInfo country = null)
    {
        var hints = new List<JumbleSessionHint>();

        if (artist is { Popularity: not null })
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Popularity, $"- They have a popularity of **{artist.Popularity}** out of 100"));
        }

        if (artist?.ArtistGenres != null && artist.ArtistGenres.Any())
        {
            var random = RandomNumberGenerator.GetInt32(artist.ArtistGenres.Count);
            var genre = artist.ArtistGenres.ToList()[random];
            hints.Add(new JumbleSessionHint(JumbleHintType.Genre, $"- One of their genres is **{genre.Name}**"));
        }

        if (artist?.StartDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.StartDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.StartDate, $"- They were born <t:{dateValue}:D> {ArtistsService.IsArtistBirthday(artist.StartDate)}"));
            }
            else
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.StartDate, $"- They started on <t:{dateValue}:D>"));
            }
        }

        if (artist?.EndDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.EndDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.EndDate, $"- They passed away on <t:{dateValue}:D>"));
            }
            else
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.EndDate, $"- They stopped on <t:{dateValue}:D>"));
            }
        }

        if (!string.IsNullOrWhiteSpace(artist?.Disambiguation) && !artist.Disambiguation.Contains(artist.Name, StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Disambiguation, $"- They might be described as **{artist.Disambiguation}**"));
        }

        if (!string.IsNullOrWhiteSpace(artist?.Type))
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Type, $"- They are a **{artist.Type.ToLower()}**"));
        }

        if (artist?.CountryCode != null && country != null)
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Country, $"- Their country has this flag: :flag_{country.Code.ToLower()}:"));
        }

        return hints;
    }

    private static List<JumbleSessionHint> GetRandomAlbumHints(Album album, Artist artist, CountryInfo country = null)
    {
        var hints = new List<JumbleSessionHint>();

        if (album is { Popularity: not null })
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Popularity, $"- Album has a popularity of **{album.Popularity}** out of 100"));
        }

        if (album is { Label: not null })
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Label, $"- Album label is **{album.Label}**"));
        }

        if (album is { ReleaseDate: not null })
        {
            hints.Add(album.ReleaseDatePrecision == "year"
                ? new JumbleSessionHint(JumbleHintType.Popularity, $"- Album was released in **{album.ReleaseDate}**")
                : new JumbleSessionHint(JumbleHintType.Popularity, $"- Album was released on **{album.ReleaseDate}**"));
        }

        if (album is { AppleMusicShortDescription: not null } &&
            !album.AppleMusicShortDescription.Contains("spatial", StringComparison.OrdinalIgnoreCase) &&
            !album.AppleMusicShortDescription.Contains("apple music", StringComparison.OrdinalIgnoreCase) &&
            !album.AppleMusicShortDescription.Contains(album.Name, StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.AppleMusicDescription, $"- *{album.AppleMusicShortDescription}*"));
        }

        if (artist?.ArtistGenres != null && artist.ArtistGenres.Any())
        {
            var random = RandomNumberGenerator.GetInt32(artist.ArtistGenres.Count);
            var genre = artist.ArtistGenres.ToList()[random];
            hints.Add(new JumbleSessionHint(JumbleHintType.Genre, $"- One of the artist their genres is **{genre.Name}**"));
        }

        if (artist?.StartDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.StartDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.StartDate, $"- Artist was born <t:{dateValue}:D> {ArtistsService.IsArtistBirthday(artist.StartDate)}"));
            }
            else
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.StartDate, $"- Artist started on <t:{dateValue}:D>"));
            }
        }

        if (artist?.EndDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.EndDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.EndDate, $"- Artist passed away on <t:{dateValue}:D>"));
            }
            else
            {
                hints.Add(new JumbleSessionHint(JumbleHintType.EndDate, $"- Artist stopped on <t:{dateValue}:D>"));
            }
        }

        if (!string.IsNullOrWhiteSpace(artist?.Disambiguation) && !artist.Disambiguation.Contains(artist.Name, StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Disambiguation, $"- Artist might be described as **{artist.Disambiguation}**"));
        }

        if (!string.IsNullOrWhiteSpace(artist?.Type))
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Type, $"- Artist is a **{artist.Type.ToLower()}**"));
        }

        if (artist?.CountryCode != null && country != null)
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Country, $"- Artist their country has this flag: :flag_{country.Code.ToLower()}:"));
        }

        return hints;
    }

    private static string JumbleWords(string input)
    {
        var words = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var jumbledWords = words.Select(JumbleWord);

        return string.Join(" ", jumbledWords);
    }

    private static string JumbleWord(string word)
    {
        var letters = word.ToCharArray();

        for (var i = letters.Length - 1; i > 0; i--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(0, i + 1);
            (letters[i], letters[swapIndex]) = (letters[swapIndex], letters[i]);
        }

        return new string(letters);
    }

    public static bool AnswerIsRight(JumbleSession game, string messageContent)
    {
        var userAnswer = CleanString(messageContent);
        var correctAnswer = CleanString(game.CorrectAnswer);

        return userAnswer.Contains(correctAnswer, StringComparison.OrdinalIgnoreCase) ||
               correctAnswer.Equals(userAnswer, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanString(string input)
    {
        var normalizedString = input.Trim().Replace("-", "").Replace(" ", "").Normalize(NormalizationForm.FormD);

        normalizedString = normalizedString
            .Replace("Ø", "O")
            .Replace("ß", "ss")
            .Replace("æ", "ae")
            .Replace("Æ", "Ae")
            .Replace("ø", "o")
            .Replace("å", "a")
            .Replace("Λ", "A")
            .Replace("Å", "A")
            .Replace("?", "")
            .Replace("’", "'")
            .Replace("‘", "'");

        var stringBuilder = new StringBuilder();
        foreach (var c in normalizedString)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static int GetLevenshteinDistance(string source1, string source2)
    {
        var source1Length = source1.Length;
        var source2Length = source2.Length;

        var matrix = new int[source1Length + 1, source2Length + 1];

        if (source1Length == 0)
        {
            return source2Length;
        }

        if (source2Length == 0)
        {
            return source1Length;
        }

        for (var i = 0; i <= source1Length; matrix[i, 0] = i++) { }
        for (var j = 0; j <= source2Length; matrix[0, j] = j++) { }
        for (var i = 1; i <= source1Length; i++)
        {
            for (var j = 1; j <= source2Length; j++)
            {
                var cost = (source2[j - 1] == source1[i - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[source1Length, source2Length];
    }

    public async Task<SKBitmap> GetSkImage(string url, string albumName, string artistName, int sessionId)
    {
        SKBitmap coverImage;
        var localPath = ChartService.AlbumUrlToCacheFilePath(albumName, artistName);

        if (localPath != null && File.Exists(localPath))
        {
            coverImage = SKBitmap.Decode(localPath);
        }
        else
        {
            var bytes = await this._client.GetByteArrayAsync(url);
            await using var stream = new MemoryStream(bytes);
            coverImage = SKBitmap.Decode(stream);

            await ChartService.SaveImageToCache(coverImage, localPath);
        }

        this._cache.Set(CacheKeyForJumbleSessionImage(sessionId), coverImage, TimeSpan.FromMinutes(2));

        return coverImage;
    }

    public async Task<SKBitmap> GetImageFromCache(int sessionId)
    {
        if (this._cache.TryGetValue(CacheKeyForJumbleSessionImage(sessionId), out SKBitmap image))
        {
            return image;
        }
        
        return null;
    }

    public static SKBitmap BlurCoverImage(SKBitmap coverImage, float pixelPercentage)
    {
        var width = coverImage.Width;
        var height = coverImage.Height;
        var pixelatedBitmap = new SKBitmap(width, height);

        var pixelSize = (int)(Math.Min(width, height) * pixelPercentage);

        using var canvas = new SKCanvas(pixelatedBitmap);
        for (var y = 0; y < height; y += pixelSize)
        {
            for (var x = 0; x < width; x += pixelSize)
            {
                var offsetX = Math.Min(pixelSize, width - x);
                var offsetY = Math.Min(pixelSize, height - y);
                var rect = new SKRect(x, y, x + offsetX, y + offsetY);
                var color = coverImage.GetPixel(x, y);

                using var paint = new SKPaint();
                paint.Color = color;
                canvas.DrawRect(rect, paint);
            }
        }

        return pixelatedBitmap;
    }

    public async Task<JumbleUserStats> GetJumbleUserStats(int userId, ulong discordUserId, JumbleType jumbleType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var jumbleSessions = await (
                db.JumbleSessions
                    .Where(w => w.StarterUserId == userId && w.JumbleType == jumbleType)
                    .Union(
                        db.JumbleSessions
                            .Where(w => w.Answers.Any(a => a.DiscordUserId == discordUserId))
                    )
            )
            .Where(w => w.JumbleType == jumbleType)
            .Include(i => i.Answers)
            .Include(i => i.Hints)
            .ToListAsync();

        if (!jumbleSessions.Any())
        {
            return null;
        }

        var gamesAnswered = jumbleSessions.Count(w => w.Answers.Any(a => a.DiscordUserId == discordUserId));
        var gamesWon = jumbleSessions.Count(w => w.Answers.Any(a => a.Correct && a.DiscordUserId == discordUserId));

        var userAnswers = jumbleSessions
            .SelectMany(s => s.Answers.Where(a => a.DiscordUserId == discordUserId))
            .ToList();

        var correctAnswers = userAnswers.Where(a => a.Correct).ToList();

        return new JumbleUserStats
        {
            TotalGamesPlayed = jumbleSessions.Count,
            GamesStarted = jumbleSessions.Count(c => c.StarterUserId == userId),
            GamesAnswered = gamesAnswered,
            TotalAnswers = userAnswers.Count,
            GamesWon = gamesWon,
            WinRate = gamesAnswered > 0 ? (decimal)gamesWon / gamesAnswered * 100 : 0,
            AvgHintsShown = (decimal)jumbleSessions.Average(s => s.Hints.Count(h => h.HintShown)),
            AvgAnsweringTime = userAnswers.Any()
                ? (decimal)userAnswers.Average(a => (a.DateAnswered - a.JumbleSession.DateStarted).TotalSeconds)
                : 0,
            AvgCorrectAnsweringTime = correctAnswers.Any()
                ? (decimal)correctAnswers.Average(a => (a.DateAnswered - a.JumbleSession.DateStarted).TotalSeconds)
                : 0,
            AvgAttemptsUntilCorrect = jumbleSessions.Any(s => s.Answers.Any(a => a.DiscordUserId == discordUserId && a.Correct)) ?
                (decimal)jumbleSessions
                    .Where(s => s.Answers.Any(a => a.DiscordUserId == discordUserId && a.Correct))
                    .Average(s => s.Answers.Count(a => a.DiscordUserId == discordUserId &&
                                                       a.DateAnswered <= s.Answers.FirstOrDefault(ca => ca.DiscordUserId == discordUserId && ca.Correct)?.DateAnswered)) : 0
        };
    }

    public async Task<JumbleGuildStats> GetJumbleGuildStats(ulong discordGuildId, JumbleType jumbleType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var jumbleSessions = await db.JumbleSessions
            .Where(w => w.DiscordGuildId == discordGuildId && w.JumbleType == jumbleType)
            .Include(i => i.Answers)
            .Include(i => i.Hints)
            .ToListAsync();

        if (!jumbleSessions.Any())
        {
            return null;
        }

        var allAnswers = jumbleSessions
            .SelectMany(s => s.Answers)
            .ToList();

        var correctAnswers = allAnswers
            .Where(a => a.Correct)
            .ToList();

        var channels = jumbleSessions
            .GroupBy(g => g.DiscordChannelId)
            .OrderByDescending(o => o.Count());

        return new JumbleGuildStats
        {
            TotalGamesPlayed = jumbleSessions.Count,
            GamesSolved = jumbleSessions.Count(w => w.Answers.Any(a => a.Correct)),
            TotalAnswers = allAnswers.Count,
            AvgHintsShown = (decimal)jumbleSessions.Average(s => s.Hints.Count(h => h.HintShown)),
            TotalReshuffles = jumbleSessions.Sum(s => s.Reshuffles),
            AvgAnsweringTime = allAnswers.Any()
                ? (decimal)allAnswers.Average(a => (a.DateAnswered - a.JumbleSession.DateStarted).TotalSeconds)
                : 0,
            AvgCorrectAnsweringTime = correctAnswers.Any()
                ? (decimal)correctAnswers.Average(a => (a.DateAnswered - a.JumbleSession.DateStarted).TotalSeconds)
                : 0,
            AvgAttemptsUntilCorrect = (decimal)jumbleSessions
                .Where(s => s.Answers.Any(a => a.Correct))
                .Average(s => s.Answers.Count(a => a.DateAnswered <= s.Answers.First(ca => ca.Correct).DateAnswered)),
            Channels = channels.Select(s => new JumbleGuildStatChannel
            {
                Id = s.Key.GetValueOrDefault(),
                Count = s.Count()
            }).ToList()
        };
    }
}
