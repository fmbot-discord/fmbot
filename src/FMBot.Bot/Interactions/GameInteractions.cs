using System;
using System.Collections.Generic;
using System.Linq;
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
    SettingService settingService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Game.AddJumbleHint)]
    public async Task JumbleAddHint(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleAddHint(new ContextModel(this.Context), parsedGameId);

        if (response.CommandResponse == CommandResponse.NotFound)
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            return;
        }

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleUnblur)]
    public async Task JumbleUnblur(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleUnblur(new ContextModel(this.Context), parsedGameId);

        if (response.CommandResponse == CommandResponse.NotFound)
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            return;
        }

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleReshuffle)]
    public async Task JumbleReshuffle(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var response = await gameBuilders.JumbleReshuffle(new ContextModel(this.Context), parsedGameId);

        if (response.CommandResponse == CommandResponse.NotFound)
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            return;
        }

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleGiveUp)]
    [UsernameSetRequired]
    public async Task JumbleGiveUp(string gameId)
    {
        var parsedGameId = int.Parse(gameId);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = await gameBuilders.JumbleGiveUp(new ContextModel(this.Context, contextUser), parsedGameId);

        if (response.CommandResponse == CommandResponse.NotFound)
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            return;
        }

        if (response.CommandResponse == CommandResponse.NoPermission)
        {
            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
        }
        else
        {
            await this.Context.UpdateInteractionEmbed(response);
        }

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message != null && response.ReferencedMusic != null &&
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
            this.Context.DeferUpdateInBackground();
            var disableButtonsTask = this.Context.DisableButtonsAndMenus().ObserveFaults();
            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

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
            await this.Context.LogCommandUsedAsync(response, userService,
                flowCommand: jumbleTypeEnum == JumbleType.Artist ? "jumble" : "pixel");
            Statistics.JumblesPlayed.WithLabels(jumbleTypeEnum == JumbleType.Artist
                ? nameof(JumbleType.Artist)
                : nameof(JumbleType.Pixelation)).Inc();

            if (response.CommandResponse == CommandResponse.Ok)
            {
                if (message == null)
                {
                    return;
                }

                var name = await UserService.GetNameAsync(this.Context.Guild, this.Context.User);
                var playingAgainButton = new ButtonProperties("1",
                    context.Localize("jumble.playingAgain", ("user", name)), ButtonStyle.Secondary)
                {
                    Disabled = true
                };
                await disableButtonsTask;
                _ = Task.Run(() => message.ModifyAsync(m =>
                    m.Components = ReplacePlayAgainButton(message.Components, playingAgainButton)));

                if (responseId.HasValue && response.GameSessionId.HasValue)
                {
                    await gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Value);

                    await JumbleTimeExpired(context, responseId.Value, cancellationTokenSource.Token,
                        response.GameSessionId.Value, secondsToGuess);
                }
            }
            else if (response.CommandResponse != CommandResponse.Cooldown && message != null)
            {
                await disableButtonsTask;
                var playAgainButton = GameBuilders.BuildPlayAgainRow(context.Localizer, jumbleTypeEnum)
                    .Components.OfType<ButtonProperties>().First();
                await message.ModifyAsync(m =>
                    m.Components = ReplacePlayAgainButton(message.Components, playAgainButton));
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

    [ComponentInteraction(InteractionConstants.Game.JumbleShowStats)]
    [UsernameSetRequired]
    public async Task JumbleShowStats(string jumbleType)
    {
        try
        {
            this.Context.DeferInBackground(MessageFlags.Ephemeral);

            var jumbleTypeEnum = Enum.Parse<JumbleType>(jumbleType);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, contextUser);
            var userSettings = await settingService.GetOriginalContextUser(this.Context.User.Id,
                this.Context.User.Id, this.Context.Guild, this.Context.User);

            var response = await gameBuilders.GetJumbleUserStats(context, userSettings, jumbleTypeEnum);

            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService,
                flowCommand: jumbleTypeEnum == JumbleType.Artist ? "jumble" : "pixel");
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Game.JumbleStats)]
    [UsernameSetRequired]
    public async Task JumbleStats(string jumbleType, string view, string discordUserId, string requesterDiscordUserId)
    {
        try
        {
            this.Context.DeferUpdateInBackground();
            var disableButtonsTask = this.Context.DisableButtonsAndMenus().ObserveFaults();

            var jumbleTypeEnum = Enum.Parse<JumbleType>(jumbleType);
            var statsView = Enum.Parse<JumbleStatsView>(view);
            var targetDiscordUserId = ulong.Parse(discordUserId);
            var requesterId = ulong.Parse(requesterDiscordUserId);

            var contextUser = await userService.GetUserAsync(requesterId);
            var discordContextUser = await this.Context.GetUserAsync(requesterId);
            var userSettings = await settingService.GetOriginalContextUser(targetDiscordUserId, requesterId,
                this.Context.Guild, this.Context.User);

            var response = await gameBuilders.GetJumbleUserStats(
                new ContextModel(this.Context, contextUser, discordContextUser), userSettings, jumbleTypeEnum,
                view: statsView);

            await disableButtonsTask;
            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private static List<IMessageComponentProperties> ReplacePlayAgainButton(
        IReadOnlyList<IMessageComponent> messageComponents, ButtonProperties replacement)
    {
        var components = messageComponents.WithDisabled();

        foreach (var component in components)
        {
            switch (component)
            {
                case ActionRowProperties row:
                    ReplaceInRow(row);
                    break;
                case ComponentContainerProperties container:
                    container.Components = container.Components.ToList();
                    foreach (var row in container.Components.OfType<ActionRowProperties>())
                    {
                        ReplaceInRow(row);
                    }

                    break;
            }
        }

        return components;

        void ReplaceInRow(ActionRowProperties row)
        {
            row.Components = row.Components.Select(IActionRowComponentProperties (c) =>
            {
                if (c is not ButtonProperties button)
                {
                    return c;
                }

                if (button.CustomId.StartsWith(InteractionConstants.Game.JumblePlayAgain))
                {
                    return replacement;
                }

                button.Disabled = false;
                return button;
            }).ToList();
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
            m.AllowedMentions = AllowedMentionsProperties.None;
            m.Flags = MessageFlags.IsComponentsV2;
            m.Embeds = [];
            m.Components = response.GetComponentsV2();
            m.Attachments = response.Stream != null
                ? [new AttachmentProperties(response.FileName, response.Stream)]
                : null;
        });

        if (PublicProperties.UsedCommandsResponseContextId.TryGetValue(responseId, out var contextId))
        {
            await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }
}
