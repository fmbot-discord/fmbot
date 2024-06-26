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
            .Where(w => !recentJumblesHashset.Contains(w.ArtistName))
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

        var minPlaycount = recentJumbles.Count(w => w.DateStarted.Date >= today.AddDays(-2)) switch
        {
            >= 75 => 1,
            >= 40 => 2,
            >= 12 => 5,
            >= 4 => 15,
            _ => 30
        };

        var finalMinPlaycount = minPlaycount * multiplier;
        if (recentJumbles.Count(w => w.DateStarted.Date == today) >= 250)
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

    public async Task<JumbleSession> StartJumbleGame(int userId, ContextModel context, JumbleType jumbleType,
                                                     string artist, CancellationTokenSource cancellationToken)
    {
        var jumbled = JumbleWords(artist).ToUpper();

        if (jumbled.Equals(artist, StringComparison.OrdinalIgnoreCase))
        {
            jumbled = JumbleWords(artist).ToUpper();
        }

        var jumbleSession = new JumbleSession
        {
            DateStarted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            StarterUserId = userId,
            DiscordGuildId = context.DiscordGuild?.Id,
            DiscordChannelId = context.DiscordChannel.Id,
            DiscordId = context.InteractionId,
            JumbleType = jumbleType,
            CorrectAnswer = artist,
            Reshuffles = 0,
            JumbledArtist = jumbled
        };

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

    private static string CacheKeyForJumbleSessionCancellationToken(ulong channelId)
    {
        return $"jumble-session-token-{channelId}";
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

    public async Task<List<JumbleSession>> GetJumbleSessionsCountToday(int userId)
    {
        const string sql = "SELECT correct_answer, date_started FROM public.jumble_sessions " +
                           "WHERE starter_user_id = @userId LIMIT 200 ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var jumblesPlayedToday = await connection.QueryAsync<JumbleSession>(sql, new
        {
            userId
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

    public List<JumbleSessionHint> GetJumbleArtistHints(Artist artist, long userPlaycount, CountryInfo country = null)
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

    public List<JumbleSessionHint> GetJumbleAlbumHints(Album album, Artist artist, long userPlaycount, CountryInfo country = null)
    {
        var hints = GetRandomArtistHints(artist, country);
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

    public async Task JumbleReshuffleArtist(JumbleSession game)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        game.JumbledArtist = JumbleWords(game.CorrectAnswer).ToUpper();
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
            hints.Add(new JumbleSessionHint(JumbleHintType.Popularity, $"- Album is released under label **{album.Label}**"));
        }

        if (album is { ReleaseDate: not null })
        {
            hints.Add(new JumbleSessionHint(JumbleHintType.Popularity, $"- Album has been released in **{album.ReleaseDate}**"));
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
        var cleanedAnswer = CleanString(messageContent);
        var cleanedArtist = CleanString(game.CorrectAnswer);

        return cleanedArtist.Equals(cleanedAnswer, StringComparison.OrdinalIgnoreCase);
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
            .Replace("Å", "A")
            .Replace("?", "");

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
                        !recentJumblesHashset.Contains(w.ArtistName))
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

        var minPlaycount = recentJumbles.Count(w => w.DateStarted.Date >= today.AddDays(-2)) switch
        {
            >= 75 => 1,
            >= 40 => 2,
            >= 12 => 5,
            >= 4 => 15,
            _ => 30
        };

        var finalMinPlaycount = minPlaycount * multiplier;
        if (recentJumbles.Count(w => w.DateStarted.Date == today) >= 250)
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
                    .Where(w => !recentJumblesHashset.Contains(w.ArtistName))
                    .OrderByDescending(o => o.UserPlaycount)
                    .ElementAtOrDefault(fallBackIndex);
            }

            return fallbackAlbum;
        }

        var randomIndex = RandomNumberGenerator.GetInt32(eligibleAlbums.Count);
        return eligibleAlbums[randomIndex];
    }

    public async Task<SKBitmap> GetSkImage(string url, string albumName, string artistName)
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

            await ChartService.OverwriteCache(stream, localPath);
        }

        return coverImage;
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
}
