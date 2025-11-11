using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using NetCord;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class InteractionContextExtensions
{
    public static void LogCommandUsed(this ApplicationCommandContext context,
        CommandResponse commandResponse = CommandResponse.Ok)
    {
        string commandName = null;
        if (context.Interaction is SocketSlashCommand socketSlashCommand)
        {
            commandName = socketSlashCommand.CommandName;
        }

        if (context.Interaction is SocketMessageComponent socketMessageComponent)
        {
            var customId = socketMessageComponent.Data?.CustomId;

            if (customId != null)
            {
                var parts = customId.Split('-');

                if (parts.Length >= 2)
                {
                    commandName = parts[0] + '-' + parts[1];
                }
            }
        }

        if (context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
            !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            Log.Information(
                "SlashCommandUsed: {discordUserName} / {discordUserId} | UserApp | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, commandResponse, commandName);
        }
        else
        {
            Log.Information(
                "SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse,
                commandName);
        }

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, commandResponse);
    }

    public static async Task HandleCommandException(this ApplicationCommandContext context, Exception exception,
        string message = null, bool sendReply = true, bool deferFirst = false)
    {
        var referenceId = CommandContextExtensions.GenerateRandomCode();

        var commandName = context.Interaction switch
        {
            SocketSlashCommand socketSlashCommand => socketSlashCommand.CommandName,
            SocketUserCommand socketUserCommand => socketUserCommand.CommandName,
            SocketInteraction socketInteraction => "ButtonInteraction",
            _ => null
        };

        Log.Error(exception,
            "SlashCommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
            referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id,
            CommandResponse.Error, message, commandName);

        if (sendReply)
        {
            if (deferFirst)
            {
                await context.Interaction.DeferAsync(ephemeral: true);
            }

            if (exception?.Message != null &&
                exception.Message.Contains("50013: Missing Permissions", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                    !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
                {
                    await context.Interaction.FollowupAsync(
                        "Error while replying: You are missing permissions, so the bot can't reply to your commands.\n" +
                        "Make sure you have permission to 'Embed links' and 'Attach Images'", ephemeral: true);
                }
                else
                {
                    await context.Interaction.FollowupAsync("Error while replying: The bot is missing permissions.\n" +
                                                            "Make sure it has permission to 'Embed links' and 'Attach Images'",
                        ephemeral: true);
                }
            }
            else
            {
                await context.Interaction.FollowupAsync(
                    $"Sorry, something went wrong while trying to process `{commandName}`. Please try again later.\n" +
                    $"*Reference id: `{referenceId}`*", ephemeral: true);
            }
        }

        PublicProperties.UsedCommandsErrorReferences.TryAdd(context.Interaction.Id, referenceId);
    }

    public static void LogCommandWithLastFmError(this IInteractionContext context, ResponseStatus? responseStatus)
    {
        string commandName = null;
        if (context.Interaction is SocketSlashCommand socketSlashCommand)
        {
            commandName = socketSlashCommand.CommandName;
        }

        Log.Error(
            "SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
            context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id,
            CommandResponse.LastFmError, commandName);

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, CommandResponse.LastFmError);
    }

    public static async Task SendResponse(this ApplicationCommandContext context, InteractiveService interactiveService,
        ResponseModel response, bool ephemeral = false, ResponseModel extraResponse = null)
    {
        var embeds = new[] { response.Embed };
        if (extraResponse != null)
        {
            embeds = [response.Embed, extraResponse.Embed?.Build()];
        }

        switch (response.ResponseType)
        {
            case ResponseType.Text:
                await context.Interaction.RespondAsync(response.Text, allowedMentions: AllowedMentions.None,
                    ephemeral: ephemeral, components: response.Components?.Build());
                break;
            case ResponseType.Embed:
                await context.Interaction.RespondAsync(null, embeds,
                    ephemeral: ephemeral, components: response.Components?.Build());
                break;
            case ResponseType.ComponentsV2:
                await context.Interaction.RespondAsync(ephemeral: ephemeral,
                    components: response.ComponentsV2?.Build(),
                    allowedMentions: AllowedMentions.None);
                break;
            case ResponseType.ImageWithEmbed:
                response.FileName =
                    StringExtensions.ReplaceInvalidChars(response.FileName);
                await context.Interaction.RespondWithFileAsync(
                    new FileAttachment(response.Stream, response.FileName, response.FileDescription, response.Spoiler),
                    null,
                    [response.Embed?.Build()],
                    ephemeral: ephemeral,
                    components: response.Components?.Build());
                break;
            case ResponseType.Paginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator.Build(),
                    (SocketInteraction)context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    ephemeral: ephemeral);
                break;
            case ResponseType.ComponentPaginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.ComponentPaginator.Build(),
                    (SocketInteraction)context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    ephemeral: ephemeral);
                break;
            case ResponseType.SupporterRequired:
                await context.Interaction.RespondWithPremiumRequiredAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Interaction.Id))
        {
            PublicProperties.UsedCommandsHintShown.Add(context.Interaction.Id);
        }

        if (response.EmoteReactions != null && response.EmoteReactions.Length != 0 &&
            response.EmoteReactions.FirstOrDefault()?.Length > 0 &&
            response.CommandResponse == CommandResponse.Ok &&
            context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            try
            {
                var message = await context.Interaction.GetOriginalResponseAsync();
                await GuildService.AddReactionsAsync(message, response.EmoteReactions);
            }
            catch (Exception e)
            {
                await context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                _ = interactiveService.DelayedDeleteMessageAsync(
                    await context.Interaction.FollowupAsync(
                        $"Could not add automatic emoji reactions.\n" +
                        $"-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`."),
                    TimeSpan.FromSeconds(30));
            }
        }
    }

    public static async Task<ulong?> SendFollowUpResponse(this IInteractionContext context,
        InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
    {
        ulong? responseId = null;

        switch (response.ResponseType)
        {
            case ResponseType.Text:
                var text = await context.Interaction.FollowupAsync(response.Text, allowedMentions: AllowedMentions.None,
                    ephemeral: ephemeral, components: response.Components?.Build());
                responseId = text.Id;
                break;
            case ResponseType.Embed:
                var embed = await context.Interaction.FollowupAsync(null, [response.Embed],
                    ephemeral: ephemeral, components: response.Components?.Build());
                responseId = embed.Id;
                break;
            case ResponseType.ComponentsV2:
                if (response.Stream is { Length: > 0 })
                {
                    response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                    var componentImage = await context.Interaction.FollowupWithFileAsync(
                        new FileAttachment(response.Stream, response.FileName, response.FileDescription, response.Spoiler),
                        components: response.ComponentsV2?.Build(),
                        ephemeral: ephemeral,
                        allowedMentions: AllowedMentions.None);

                    await response.Stream.DisposeAsync();
                    responseId = componentImage.Id;
                }
                else
                {
                    var components = await context.Interaction.FollowupAsync(ephemeral: ephemeral,
                        components: response.ComponentsV2?.Build(),
                        allowedMentions: AllowedMentions.None);
                    responseId = components.Id;
                }

                break;
            case ResponseType.Paginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator.Build(),
                    (SocketInteraction)context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    InteractionResponseType.DeferredChannelMessageWithSource,
                    ephemeral: ephemeral);
                break;
            case ResponseType.ComponentPaginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.ComponentPaginator.Build(),
                    (SocketInteraction)context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    InteractionResponseType.DeferredChannelMessageWithSource,
                    ephemeral: ephemeral);
                break;
            case ResponseType.ImageWithEmbed:
                response.FileName =
                    StringExtensions.ReplaceInvalidChars(response.FileName);
                var imageWithEmbed = await context.Interaction.FollowupWithFileAsync(
                    new FileAttachment(response.Stream, response.FileName, response.FileDescription, response.Spoiler),
                    null,
                    [response.Embed?.Build()],
                    ephemeral: ephemeral,
                    components: response.Components?.Build());

                await response.Stream.DisposeAsync();
                responseId = imageWithEmbed.Id;
                break;
            case ResponseType.ImageOnly:
                response.FileName =
                    StringExtensions.ReplaceInvalidChars(response.FileName);
                var image = await context.Interaction.FollowupWithFileAsync(
                    new FileAttachment(response.Stream, response.FileName, response.FileDescription, response.Spoiler),
                    ephemeral: ephemeral);

                await response.Stream.DisposeAsync();
                responseId = image.Id;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (responseId.HasValue)
        {
            PublicProperties.UsedCommandsResponseMessageId.TryAdd(context.Interaction.Id, responseId.Value);
            PublicProperties.UsedCommandsResponseContextId.TryAdd(responseId.Value, context.Interaction.Id);
        }

        if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Interaction.Id))
        {
            PublicProperties.UsedCommandsHintShown.Add(context.Interaction.Id);
        }

        if (response.EmoteReactions != null && response.EmoteReactions.Length != 0 &&
            response.EmoteReactions.FirstOrDefault()?.Length > 0 &&
            response.CommandResponse == CommandResponse.Ok &&
            context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            try
            {
                var message = await context.Interaction.GetOriginalResponseAsync();
                await GuildService.AddReactionsAsync(message, response.EmoteReactions);
            }
            catch (Exception e)
            {
                await context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                _ = interactiveService.DelayedDeleteMessageAsync(
                    await context.Interaction.FollowupAsync(
                        $"Could not add automatic emoji reactions.\n" +
                        $"-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`."),
                    TimeSpan.FromSeconds(30));
            }
        }

        return responseId;
    }

    public static async Task UpdateInteractionEmbed(this ApplicationCommandContext context, ResponseModel response,
        InteractiveService interactiveService = null, bool defer = true)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        if (response.ResponseType == ResponseType.Paginator)
        {
            if (defer)
            {
                await context.Interaction.DeferAsync();
            }

            await context.ModifyPaginator(interactiveService, message, response);
            return;
        }

        await context.ModifyMessage(message, response, defer);
    }


    public static async Task DisableActionRows(this IInteractionContext context, bool interactionEdit = false,
        string specificButtonOnly = null)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var componentBuilder = message.Components.ToBuilder();
        foreach (var row in componentBuilder.Components)
        {
            if (row is not ActionRowBuilder rowBuilder)
            {
                continue;
            }

            foreach (var rowComponent in rowBuilder.Components)
            {
                if (rowComponent is not ButtonBuilder button)
                {
                    continue;
                }

                if (button.Style == ButtonStyle.Link)
                {
                    continue;
                }

                if (specificButtonOnly != null && button.CustomId != specificButtonOnly)
                {
                    continue;
                }

                button.IsDisabled = true;
            }
        }

        await ModifyComponents(context, message, componentBuilder, interactionEdit);
    }

    public static async Task DisableInteractionButtons(this IInteractionContext context, bool interactionEdit = false,
        string specificButtonOnly = null, bool addLoaderToSpecificButton = false)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var newComponents = new ActionRowProperties();
        foreach (var component in message.Components)
        {
            if (component is not ActionRowComponent actionRowComponent)
            {
                continue;
            }

            foreach (var subComponent in actionRowComponent.Components)
            {
                if (subComponent is ButtonComponent buttonComponent)
                {
                    if (specificButtonOnly != null && specificButtonOnly == buttonComponent.CustomId)
                    {
                        if (addLoaderToSpecificButton)
                        {
                            newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId,
                                buttonComponent.Style,
                                EmojiProperties.Custom(DiscordConstants.Loading), buttonComponent.Url, true);
                        }
                        else
                        {
                            newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId,
                                buttonComponent.Style,
                                buttonComponent.Emote, buttonComponent.Url, true);
                        }
                    }
                    else if (specificButtonOnly == null)
                    {
                        newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId,
                            buttonComponent.Style,
                            buttonComponent.Emote, buttonComponent.Url, true);
                    }
                    else
                    {
                        newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId,
                            buttonComponent.Style,
                            buttonComponent.Emote, buttonComponent.Url, false);
                    }
                }
            }
        }

        await ModifyComponents(context, message, newComponents, interactionEdit);
    }

    public static async Task AddButton(this IInteractionContext context, ButtonBuilder extraButtonBuilder = null)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var newComponents = new ActionRowProperties();
        foreach (var component in message.Components)
        {
            if (component is not ActionRowComponent actionRowComponent)
            {
                continue;
            }

            foreach (var subComponent in actionRowComponent.Components)
            {
                if (subComponent is ButtonComponent buttonComponent)
                {
                    newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId, buttonComponent.Style,
                        buttonComponent.Emote, buttonComponent.Url, true);
                }
            }
        }

        newComponents.WithButton(extraButtonBuilder);

        await ModifyComponents(context, message, newComponents);
    }

    public static async Task EnableInteractionButtons(this IInteractionContext context)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var newComponents = new ActionRowProperties();
        foreach (var component in message.Components)
        {
            if (component is not ActionRowComponent actionRowComponent)
            {
                continue;
            }

            foreach (var subComponent in actionRowComponent.Components)
            {
                if (subComponent is ButtonComponent buttonComponent)
                {
                    newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId, buttonComponent.Style,
                        buttonComponent.Emote, buttonComponent.Url);
                }
            }
        }

        await ModifyComponents(context, message, newComponents);
    }

    public static async Task UpdateMessageEmbed(this IInteractionContext context, ResponseModel response,
        string messageId, bool interactionEdit = false, bool defer = true)
    {
        var parsedMessageId = ulong.Parse(messageId);
        var msg = await context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        await context.ModifyMessage(message, response, defer);
    }

    public static async Task ModifyComponents(this IInteractionContext context, IUserMessage message,
        ComponentBuilder newComponents, bool interactionEdit = false)
    {
        if ((context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
             !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall)) ||
            interactionEdit)
        {
            await context.Interaction.ModifyOriginalResponseAsync(m => m.Components = newComponents.Build());
        }
        else
        {
            await message.ModifyAsync(m => m.Components = newComponents.Build());
        }
    }

    public static async Task ModifyComponents(this IInteractionContext context, IUserMessage message,
        ComponentBuilderV2 newComponents, bool interactionEdit = false)
    {
        if ((context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
             !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall)) ||
            interactionEdit)
        {
            await context.Interaction.ModifyOriginalResponseAsync(m => m.Components = newComponents.Build());
        }
        else
        {
            await message.ModifyAsync(m => m.Components = newComponents.Build());
        }
    }

    public static async Task ModifyMessage(this IInteractionContext context, IUserMessage message,
        ResponseModel response, bool defer = true, bool interactionEdit = false)
    {
        if ((context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
             !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall)) ||
            interactionEdit)
        {
            await context.Interaction.ModifyOriginalResponseAsync(m =>
            {
                m.Components = response.ResponseType == ResponseType.ComponentsV2
                    ? response.ComponentsV2?.Build()
                    : response.Components?.Build();
                m.Embed = response.ResponseType == ResponseType.ComponentsV2 ? null : response.Embed?.Build();
                m.Attachments = response.Stream != null
                    ? new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>
                    {
                        new(response.Stream,
                            response.Spoiler ? $"SPOILER_{response.FileName}" : $"{response.FileName}")
                    })
                    : null;
                m.AllowedMentions = AllowedMentions.None;
            });
        }
        else
        {
            await message.ModifyAsync(m =>
            {
                m.Components = response.ResponseType == ResponseType.ComponentsV2
                    ? response.ComponentsV2?.Build()
                    : response.Components?.Build();
                m.Embed = response.ResponseType == ResponseType.ComponentsV2 ? null : response.Embed?.Build();
                m.Attachments = response.Stream != null
                    ? new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>
                    {
                        new(response.Stream,
                            response.Spoiler ? $"SPOILER_{response.FileName}" : $"{response.FileName}")
                    })
                    : null;
                m.AllowedMentions = AllowedMentions.None;
            });
        }

        if (defer)
        {
            await context.Interaction.DeferAsync();
        }
    }

    private static async Task ModifyPaginator(this IInteractionContext context, InteractiveService interactiveService,
        IUserMessage message, ResponseModel response)
    {
        if (context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
            !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            _ = interactiveService.SendPaginatorAsync(
                response.StaticPaginator.Build(),
                context.Interaction,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                InteractionResponseType.DeferredUpdateMessage);
        }
        else
        {
            if (message.Attachments != null && message.Attachments.Any())
            {
                await message.ModifyAsync(m => { m.Attachments = null; });
            }

            _ = interactiveService.SendPaginatorAsync(
                response.StaticPaginator.Build(),
                message,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
        }

        return;
    }
}
