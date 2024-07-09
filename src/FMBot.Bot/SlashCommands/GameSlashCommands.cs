using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
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

    [ComponentInteraction($"{InteractionConstants.Game.JumbleUnblur}-*")]
    public async Task JumbleUnblur(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await this._gameBuilders.JumbleUnblur(new ContextModel(this.Context), parsedGameId);

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

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
        if (message != null && PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
        {
            await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }
}
