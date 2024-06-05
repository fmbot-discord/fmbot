using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services;

public class GameService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public const int SecondsToGuess = 25;

    public GameService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
    }

    public async Task<(string artist, long userPlaycount)> PickArtistForJumble(List<TopArtist> topArtists)
    {
        var total = topArtists.Count(w => w.UserPlaycount >= 5);
        var random = RandomNumberGenerator.GetInt32(total);

        return (topArtists[random].ArtistName, topArtists[random].UserPlaycount);
    }

    public async Task<GameModel> StartJumbleGame(int userId, ContextModel context, GameType gameType, string artist)
    {
        var game = new GameModel
        {
            DateStarted = DateTime.UtcNow,
            StarterUserId = userId,
            DiscordGuildId = context.DiscordGuild?.Id,
            DiscordChannelId = context.DiscordChannel.Id,
            DiscordId = context.InteractionId,
            GameType = gameType,
            CorrectAnswer = artist,
            GameId = 1,
            Reshuffles = 0,
            JumbledArtist = JumbleWords(artist).ToUpper()
        };


        // add to database

        this._cache.Set(
            CacheKeyForJumbleGame(context.DiscordChannel.Id), game, TimeSpan.FromSeconds(SecondsToGuess + 1));
        PublicProperties.GameChannel.Add(context.DiscordChannel.Id);

        return game;
    }

    public static string CacheKeyForJumbleGame(ulong channelId)
    {
        return $"jumble-game-{channelId}";
    }

    public async Task<GameModel> GetGame(ulong channelId, int? gameId = null)
    {
        if (!this._cache.TryGetValue(CacheKeyForJumbleGame(channelId), out GameModel game))
        {
            return null;
        }

        return game;
    }

    public List<GameHintModel> GetJumbleHints(Artist artist, long userPlaycount, CountryInfo country = null)
    {
        var hints = GetRandomHints(artist, country);
        hints.Add(new GameHintModel(JumbleHintType.Playcount, $"- You have **{userPlaycount}** {StringExtensions.GetPlaysString(userPlaycount)} on this artist"));

        RandomNumberGenerator.Shuffle(CollectionsMarshal.AsSpan(hints));

        for (int i = 0; i < Math.Min(hints.Count, 3); i++)
        {
            hints[i].HintShown = true;
            hints[i].Order = i;
        }

        return hints;
    }

    public static string HintsToString(List<GameHintModel> hints, int count = 3)
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

    public async Task JumbleStoreShowedHints(GameModel game, List<GameHintModel> hints)
    {
        game.Hints = hints;

        // save to db
    }

    public async Task JumbleReshuffleArtist(GameModel game)
    {
        game.JumbledArtist = JumbleWords(game.CorrectAnswer).ToUpper();
        game.Reshuffles++;

        // save to db
    }

    public async Task JumbleEndGame(GameModel game, ulong discordChannelId)
    {
        game.DateEnded = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        // save to db

        this._cache.Remove(CacheKeyForJumbleGame(discordChannelId));
    }

    public async Task JumbleAddResponseId(ulong discordChannelId, ulong responseId)
    {
        var game = await GetGame(discordChannelId);
        
        game.DiscordResponseId = responseId;

        // save to db
    }

    public async Task JumbleAddAnswer(GameModel game, ulong discordUserId, string content, bool correct)
    {
        var answer = new GameAnswerModel
        {
            Answer = content,
            Correct = correct,
            DiscordUserId = discordUserId,
            GameId = game.GameId
        };

        game.Answers ??= new List<GameAnswerModel>();
        
        game.Answers.Add(answer);
        
        // add to db
    }

    private List<GameHintModel> GetRandomHints(Artist artist, CountryInfo country = null)
    {
        var hints = new List<GameHintModel>();

        if (artist is { Popularity: not null })
        {
            hints.Add(new GameHintModel(JumbleHintType.Popularity, $"- They have a popularity value of **{artist.Popularity}**"));
        }

        if (artist.ArtistGenres.Any())
        {
            var random = RandomNumberGenerator.GetInt32(artist.ArtistGenres.Count);
            var genre = artist.ArtistGenres.ToList()[random];
            hints.Add(new GameHintModel(JumbleHintType.Genre, $"- One of their genres is **{genre.Name}**"));
        }

        if (artist.StartDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.StartDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new GameHintModel(JumbleHintType.StartDate, $"- They were born <t:{dateValue}:D> {ArtistsService.IsArtistBirthday(artist.StartDate)}"));
            }
            else
            {
                hints.Add(new GameHintModel(JumbleHintType.StartDate, $"- They started on <t:{dateValue}:D>"));
            }
        }

        if (artist.EndDate != null)
        {
            var specifiedDateTime = DateTime.SpecifyKind(artist.EndDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            if (artist.Type?.ToLower() == "person")
            {
                hints.Add(new GameHintModel(JumbleHintType.EndDate, $"- They passed away on <t:{dateValue}:D>"));
            }
            else
            {
                hints.Add(new GameHintModel(JumbleHintType.EndDate, $"- They stopped on <t:{dateValue}:D>"));
            }
        }

        if (!string.IsNullOrWhiteSpace(artist.Disambiguation))
        {
            hints.Add(new GameHintModel(JumbleHintType.Disambiguation, $"- They might be described as **{artist.Disambiguation}**"));
        }

        if (!string.IsNullOrWhiteSpace(artist.Type))
        {
            hints.Add(new GameHintModel(JumbleHintType.Type, $"- They are a **{artist.Type.ToLower()}**"));
        }

        if (artist.CountryCode != null && country != null)
        {
            hints.Add(new GameHintModel(JumbleHintType.Country, $"- Their country has this flag: :flag_{country.Code.ToLower()}:"));
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

    public static bool AnswerIsRight(GameModel game, string messageContent)
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
            .Replace("Å", "A");

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
