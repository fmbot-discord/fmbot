using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services;

public class GameService
{
    public async Task<string> PickArtistForJumble(List<TopArtist> topArtists)
    {
        return "The Beatles";
    }

    public async Task<(GameModel game, string jumbled)> StartJumbleGame(int userId, ulong? discordGuildId, GameType gameType, string artist)
    {
        var game = new GameModel
        {
            DateStarted = DateTime.UtcNow,
            StarterUserId = userId,
            DiscordGuildId = discordGuildId,
            GameType = gameType,
            CorrectAnswer = artist,
            HintCount = 0
        };

        var jumbled = JumbleWords(artist);



        return (game, jumbled);
    }

    //public async Task<string> GetJumbleHint(int userId, string artist)
    //{

    //    var jumbled = JumbleWords(artist);



    //    return (game, jumbled);
    //}

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
}
