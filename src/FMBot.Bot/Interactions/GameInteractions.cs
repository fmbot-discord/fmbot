using System;
using System.Threading;
using System.Threading.Tasks;
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
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class GameInteractions(
    GameBuilders gameBuilders,
    UserService userService,
    GameService gameService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Game.AddJumbleHint)]
    public async Task JumbleAddHint(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleAddHint(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleUnblur)]
    public async Task JumbleUnblur(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleUnblur(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleReshuffle)]
    public async Task JumbleReshuffle(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleReshuffle(new ContextModel(this.Context), parsedGameId);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleGiveUp)]
    public async Task JumbleGiveUp(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = await gameBuilders.JumbleGiveUp(new ContextModel(this.Context, contextUser), parsedGameId);

        if (response.CommandResponse == CommandResponse.NoPermission)
        {
            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
        }
        else
        {
            await this.Context.UpdateInteractionEmbed(response);
        }

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message != null &&
            PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
        {
            await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }

    [ComponentInteraction(InteractionConstants.Game.JumblePlayAgain)]
    [UsernameSetRequired]
    public async Task JumblePlayAgain(string jumbleType)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableInteractionButtons();

            var jumbleTypeEnum = (JumbleType)Enum.Parse(typeof(JumbleType), jumbleType);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, contextUser);

            var cancellationTokenSource = new CancellationTokenSource();

            ResponseModel response;
            var secondsToGuess = GameService.JumbleSecondsToGuess;
            if (jumbleTypeEnum == JumbleType.Artist)
            {
                response = await gameBuilders.StartArtistJumble(context, contextUser.UserId,
                    cancellationTokenSource);
            }
            else
            {
                secondsToGuess = GameService.PixelationSecondsToGuess;
                response = await gameBuilders.StartPixelJumble(context, contextUser.UserId,
                    cancellationTokenSource);
            }

            var responseId = await this.Context.SendFollowUpResponse(interactivity, response, userService,
                ephemeral: response.CommandResponse != CommandResponse.Ok);
            await this.Context.LogCommandUsedAsync(response, userService);

            if (response.CommandResponse == CommandResponse.Ok)
            {
                var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
                if (message == null)
                {
                    return;
                }

                var name = await UserService.GetNameAsync(this.Context.Guild, this.Context.User);
                var components = new ActionRowProperties().WithButton($"{name} is playing again!", customId: "1",
                    url: null, disabled: true, style: ButtonStyle.Secondary);
                _ = Task.Run(() => message.ModifyAsync(m => m.Components = [components]));

                if (responseId.HasValue && response.GameSessionId.HasValue)
                {
                    await gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Value);

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
            await this.Context.HandleCommandException(e, userService);
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

        var response = await gameBuilders.JumbleTimeExpired(context, gameSessionId);

        if (response == null)
        {
            return;
        }

        await this.Context.Client.Rest.ModifyMessageAsync(context.DiscordChannel.Id, responseId, m =>
        {
            m.Components = [];
            m.Embeds = [response.Embed];
            m.Attachments = response.Stream != null
                ? [new AttachmentProperties(response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName, response.Stream)]
                : null;
        });

        if (PublicProperties.UsedCommandsResponseContextId.TryGetValue(responseId, out var contextId))
        {
            await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }
}
