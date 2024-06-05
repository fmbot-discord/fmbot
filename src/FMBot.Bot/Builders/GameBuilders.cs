using System.Linq;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using System.Text;
using FMBot.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using static FMBot.Bot.Resources.InteractionConstants;
using Game = Discord.Game;

namespace FMBot.Bot.Builders;

public class GameBuilders
{
    private readonly UserService _userService;
    private readonly GameService _gameService;
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;

    public GameBuilders(UserService userService, GameService gameService, ArtistsService artistsService, CountryService countryService)
    {
        this._userService = userService;
        this._gameService = gameService;
        this._artistsService = artistsService;
        this._countryService = countryService;
    }

    public static ResponseModel GameModePick(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Components = new ComponentBuilder()
                .WithButton("First correct answer wins", InteractionConstants.Game.StartJumbleFirstWins)
                .WithButton("Play as group", InteractionConstants.Game.StartJumbleGroup)
        };

        var description = new StringBuilder();

        description.AppendLine("Start jumble game");

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> StartJumbleFirstWins(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (existingGame != null && !existingGame.DateEnded.HasValue)
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription($"There is already a game in progress. Finish it or give up before starting a new one.");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);
        var artist = await this._gameService.PickArtistForJumble(topArtists);

        var game = await this._gameService.StartJumbleGame(userId, context, GameType.JumbleFirstWins, artist.artist);
        var databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        CountryInfo artistCountry = null;
        if (databaseArtist.CountryCode != null)
        {
            artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
        }

        var hints = this._gameService.GetJumbleHints(databaseArtist, artist.userPlaycount, artistCountry);
        var hintString = GameService.HintsToString(hints, 3);
        await this._gameService.JumbleStoreShowedHints(game, hints);

        BuildJumbleEmbed(response.Embed, game.JumbledArtist, hintString, hints.Count(c => c.HintShown));
        response.Components = BuildJumbleComponents(game.GameId);

        return response;
    }

    private static void BuildJumbleEmbed(EmbedBuilder embed, string jumbledArtist, string hintString, int amountOfHints, bool canBeAnswered = true)
    {
        embed.WithColor(DiscordConstants.InformationColorBlue);

        embed.WithAuthor("Guess the artist - Jumble");

        embed.WithDescription($"### `{jumbledArtist}`");

        var hintTitle = "Hints";
        if (amountOfHints > 3)
        {
            hintTitle = $"Hints + {amountOfHints - 3} extra {StringExtensions.GetHintsString(amountOfHints - 3)}";
        }
        embed.AddField(hintTitle, hintString);

        if (canBeAnswered)
        {
            embed.AddField("Add answer", "Type your answer in the chat to make a guess");
        }
    }

    private static ComponentBuilder BuildJumbleComponents(int gameId)
    {
        return new ComponentBuilder()
            .WithButton("Add hint", $"{InteractionConstants.Game.AddJumbleHint}-{gameId}", ButtonStyle.Secondary)
            .WithButton("Reshuffle", $"{InteractionConstants.Game.JumbleReshuffle}-{gameId}", ButtonStyle.Secondary)
            .WithButton("Give up", $"{InteractionConstants.Game.JumbleGiveUp}-{gameId}", ButtonStyle.Secondary);
    }

    public async Task<ResponseModel> JumbleAddHint(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (existingGame == null || existingGame.DateEnded.HasValue)
        {
            return response;
        }

        var hintString = GameService.HintsToString(existingGame.Hints, existingGame.Hints.Count(w => w.HintShown) + 1);
        await this._gameService.JumbleStoreShowedHints(existingGame, existingGame.Hints);

        BuildJumbleEmbed(response.Embed, existingGame.JumbledArtist, hintString, existingGame.Hints.Count(w => w.HintShown));
        response.Components = BuildJumbleComponents(existingGame.GameId);

        return response;
    }

    public async Task<ResponseModel> JumbleReshuffle(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (existingGame == null || existingGame.DateEnded.HasValue)
        {
            return response;
        }

        await this._gameService.JumbleReshuffleArtist(existingGame);

        var hintString = GameService.HintsToString(existingGame.Hints, existingGame.Hints.Count(w => w.HintShown));
        var hintsShown = existingGame.Hints.Count(w => w.HintShown);

        BuildJumbleEmbed(response.Embed, existingGame.JumbledArtist, hintString, hintsShown);
        response.Components = BuildJumbleComponents(existingGame.GameId);

        return response;
    }

    public async Task<ResponseModel> JumbleGiveUp(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (existingGame == null || existingGame.DateEnded.HasValue)
        {
            return response;
        }

        var hintString = GameService.HintsToString(existingGame.Hints, existingGame.Hints.Count(w => w.HintShown));
        var hintsShown = existingGame.Hints.Count(w => w.HintShown);

        BuildJumbleEmbed(response.Embed, existingGame.JumbledArtist, hintString, hintsShown, false);

        response.Embed.AddField("You gave up!", $"The correct answer was **{existingGame.CorrectAnswer}**");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        return response;
    }
}
