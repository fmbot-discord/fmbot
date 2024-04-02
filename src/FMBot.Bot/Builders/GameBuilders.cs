using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using System.Text;
using FMBot.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Services;

namespace FMBot.Bot.Builders;

public class GameBuilders
{
    private readonly UserService _userService;
    private readonly GameService _gameService;
    private readonly ArtistsService _artistsService;

    public GameBuilders(UserService userService, GameService gameService, ArtistsService artistsService)
    {
        this._userService = userService;
        this._gameService = gameService;
        this._artistsService = artistsService;
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

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);
        var artist = await this._gameService.PickArtistForJumble(topArtists);
        var game = await this._gameService.StartJumbleGame(userId, context.DiscordGuild?.Id, GameType.JumbleFirstWins, artist);

        response.Embed.WithAuthor("Guess the artist - Jumble");

        var description = new StringBuilder();
        response.Embed.WithDescription($"`{game.jumbled}`");
        response.Embed.AddField("Hint", "aasddasd");
        response.Embed.AddField("Add an answer", "Type your answer in this channel to make a guess");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }
}
