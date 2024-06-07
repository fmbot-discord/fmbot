using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

namespace FMBot.Bot.Services;

public class GameService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;

    public const int SecondsToGuess = 25;

    public GameService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
    }

    public static (string artist, long userPlaycount) PickArtistForJumble(List<TopArtist> topArtists,
        HashSet<string> jumblesPlayedToday = null)
    {
        jumblesPlayedToday ??= [];

        topArtists = topArtists.Where(w => !jumblesPlayedToday.Contains(w.ArtistName)).ToList();

        var multiplier = topArtists.Count switch
        {
            > 7000 => 8,
            > 5000 => 6,
            > 3000 => 4,
            > 2000 => 3,
            > 750 => 2,
            _ => 1
        };

        var minPlaycount = jumblesPlayedToday.Count switch
        {
            >= 120 => 1,
            >= 75 => 2,
            >= 40 => 3,
            >= 25 => 4,
            >= 12 => 5,
            >= 5 => 25,
            _ => 40
        };

        var finalMinPlaycount = minPlaycount * multiplier;
        if (jumblesPlayedToday.Count > 500)
        {
            finalMinPlaycount = 1;
        }

        var total = topArtists.Count(w => w.UserPlaycount >= finalMinPlaycount);
        Log.Information("PickArtistForJumble: {topArtistCount} top artists - {jumblesPlayedTodayCount} jumbles played today - " +
                        "{multiplier} multiplier - {minPlaycount} min playcount - {finalMinPlaycount} final min playcount",
            topArtists.Count, jumblesPlayedToday.Count,  multiplier, minPlaycount, finalMinPlaycount);

        if (total == 0)
        {
            return (null, 0);
        }

        var random = RandomNumberGenerator.GetInt32(total);

        return (topArtists[random].ArtistName, topArtists[random].UserPlaycount);
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

    public async Task<HashSet<string>> GetJumbleSessionsCountToday(int userId)
    {
        const string sql = "SELECT correct_answer FROM public.jumble_sessions " +
                           "WHERE starter_user_id = @userId AND date_started >= date_trunc('day', current_date); ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var jumblesPlayedToday = await connection.QueryAsync<string>(sql, new
        {
            userId
        });

        return jumblesPlayedToday
            .GroupBy(g => g)
            .Select(s => s.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task CancelToken(ulong channelId)
    {
        if (!this._cache.TryGetValue(CacheKeyForJumbleSessionCancellationToken(channelId), out CancellationTokenSource token))
        {
            return;
        }

        await token.CancelAsync();
    }

    public List<JumbleSessionHint> GetJumbleHints(Artist artist, long userPlaycount, CountryInfo country = null)
    {
        var hints = GetRandomHints(artist, country);
        hints.Add(new JumbleSessionHint(JumbleHintType.Playcount, $"- You have **{userPlaycount}** {StringExtensions.GetPlaysString(userPlaycount)} on this artist"));

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

    public async Task JumbleAddAnswer(JumbleSession game, ulong discordUserId, string content, bool correct)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var answer = new JumbleSessionAnswer
        {
            Answer = content,
            Correct = correct,
            DiscordUserId = discordUserId,
            JumbleSessionId = game.JumbleSessionId,
            DateAnswered = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        };

        await db.JumbleSessionAnswers.AddAsync(answer);

        await db.SaveChangesAsync();
    }

    private static List<JumbleSessionHint> GetRandomHints(Artist artist, CountryInfo country = null)
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

    public static string JumbleWords(string input)
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
}
