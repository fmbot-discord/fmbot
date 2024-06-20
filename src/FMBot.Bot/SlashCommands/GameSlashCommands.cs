using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class GameSlashCommands : InteractionModuleBase
{
    private readonly GameBuilders _gameBuilders;
    private readonly UserService _userService;

    private InteractiveService Interactivity { get; }

    public GameSlashCommands(GameBuilders gameBuilders, UserService userService, InteractiveService interactivity)
    {
        this._gameBuilders = gameBuilders;
        this._userService = userService;
        this.Interactivity = interactivity;
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
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._gameBuilders.JumbleGiveUp(new ContextModel(this.Context, contextUser), parsedGameId);

        if (response.CommandResponse == CommandResponse.NoPermission)
        {
            await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        }
        else
        {
            await this.Context.UpdateInteractionEmbed(response);
        }
    }
}
