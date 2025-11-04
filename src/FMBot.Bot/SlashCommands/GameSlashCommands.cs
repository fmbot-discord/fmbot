using System;
using System.Threading;
using System.Threading.Tasks;

using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.SlashCommands;

public class GameSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly GameBuilders _gameBuilders;
    private readonly UserService _userService;
    private readonly GameService _gameService;

    private InteractiveService Interactivity { get; }

    public GameSlashCommands(GameBuilders gameBuilders, UserService userService, InteractiveService interactivity,
        GameService gameService)
    {
        this._gameBuilders = gameBuilders;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._gameService = gameService;
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
        if (message != null &&
            PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
        {
            await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Game.JumblePlayAgain}-*")]
    [UsernameSetRequired]
    public async Task JumblePlayAgain(string jumbleType)
    {
        try
        {
            await DeferAsync();
            await this.Context.DisableInteractionButtons();

            var jumbleTypeEnum = (JumbleType)Enum.Parse(typeof(JumbleType), jumbleType);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, contextUser);

            var cancellationTokenSource = new CancellationTokenSource();

            ResponseModel response;
            var secondsToGuess = GameService.JumbleSecondsToGuess;
            if (jumbleTypeEnum == JumbleType.Artist)
            {
                response = await this._gameBuilders.StartArtistJumble(context, contextUser.UserId,
                    cancellationTokenSource);
            }
            else
            {
                secondsToGuess = GameService.PixelationSecondsToGuess;
                response = await this._gameBuilders.StartPixelJumble(context, contextUser.UserId,
                    cancellationTokenSource);
            }

            var responseId = await this.Context.SendFollowUpResponse(this.Interactivity, response,
                ephemeral: response.CommandResponse != CommandResponse.Ok);
            this.Context.LogCommandUsed(response.CommandResponse);

            if (response.CommandResponse == CommandResponse.Ok)
            {
                var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
                if (message == null)
                {
                    return;
                }

                var name = await UserService.GetNameAsync(this.Context.Guild, this.Context.User);
                var components = new ComponentBuilder().WithButton($"{name} is playing again!", customId: "1",
                    url: null, disabled: true, style: ButtonStyle.Secondary);
                _ = Task.Run(() => message.ModifyAsync(m => m.Components = components.Build()));

                if (responseId.HasValue && response.GameSessionId.HasValue)
                {
                    await this._gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Value);

                    await JumbleTimeExpired(context, responseId.Value, cancellationTokenSource.Token,
                        response.GameSessionId.Value, secondsToGuess);
                }
            }
            else if (response.CommandResponse != CommandResponse.Cooldown)
            {
                await this.Context.EnableInteractionButtons();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private async Task JumbleTimeExpired(ContextModel context, ulong responseId, CancellationToken cancellationToken,
        int gameSessionId, int secondsToGuess)
    {
        await Task.Delay(secondsToGuess * 1000, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var response = await this._gameBuilders.JumbleTimeExpired(context, gameSessionId);

        if (response == null)
        {
            return;
        }

        var msg = await this.Context.Channel.GetMessageAsync(responseId);
        if (msg is not IUserMessage message)
        {
            return;
        }

        if (PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
        {
            await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }

        await this.Context.ModifyMessage(message, response, false);
    }
}
