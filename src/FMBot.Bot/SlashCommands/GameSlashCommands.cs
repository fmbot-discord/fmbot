using System.Threading.Tasks;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class GameSlashCommands : InteractionModuleBase
{
    private readonly GameBuilders _gameBuilders;
    private readonly UserService _userService;

    public GameSlashCommands(GameBuilders gameBuilders, UserService userService)
    {
        this._gameBuilders = gameBuilders;
        this._userService = userService;
    }

    [ComponentInteraction(InteractionConstants.Game.StartJumbleFirstWins)]
    public async Task StartJumbleFirstAnswerWins()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = await this._gameBuilders.StartJumbleFirstWins(new ContextModel(this.Context), contextUser.UserId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.Game.AddJumbleHint}-*")]
    public async Task JumbleAddHint(string gameId)
    {
        var parsedGameId = int.Parse(gameId);

        var response = await this._gameBuilders.JumbleAddHint(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.Game.JumbleReshuffle}-*")]
    public async Task JumbleReshuffle(string gameId)
    {
        var parsedGameId = int.Parse(gameId);

        var response = await this._gameBuilders.JumbleReshuffle(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.Game.JumbleGiveUp}-*")]
    public async Task JumbleGiveUp(string gameId)
    {
        var parsedGameId = int.Parse(gameId);

        var response = await this._gameBuilders.JumbleGiveUp(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }
}
