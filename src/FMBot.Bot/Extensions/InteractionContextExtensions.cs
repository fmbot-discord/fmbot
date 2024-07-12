using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class InteractionContextExtensions
{
    public static void LogCommandUsed(this IInteractionContext context, CommandResponse commandResponse = CommandResponse.Ok)
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

        Log.Information("SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
            context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, commandName);

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, commandResponse);
    }

    public static async Task HandleCommandException(this IInteractionContext context, Exception exception, string message = null, bool sendReply = true, bool deferFirst = false)
    {
        var referenceId = CommandContextExtensions.GenerateRandomCode();

        var commandName = context.Interaction switch
        {
            SocketSlashCommand socketSlashCommand => socketSlashCommand.CommandName,
            SocketUserCommand socketUserCommand => socketUserCommand.CommandName,
            SocketInteraction socketInteraction => "ButtonInteraction",
            _ => null
        };

        Log.Error(exception, "SlashCommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
            referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, commandName);

        if (sendReply)
        {
            if (deferFirst)
            {
                await context.Interaction.DeferAsync(ephemeral: true);
            }

            await context.Interaction.FollowupAsync($"Sorry, something went wrong while trying to process `{commandName}`. Please try again later.\n" +
                                                    $"*Reference id: `{referenceId}`*", ephemeral: true);
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

        Log.Error("SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
            context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.LastFmError, commandName);

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, CommandResponse.LastFmError);
    }

    public static async Task SendResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
    {
        switch (response.ResponseType)
        {
            case ResponseType.Text:
                await context.Interaction.RespondAsync(response.Text, allowedMentions: AllowedMentions.None, ephemeral: ephemeral, components: response.Components?.Build());
                break;
            case ResponseType.Embed:
                await context.Interaction.RespondAsync(null, new[] { response.Embed.Build() },
                    ephemeral: ephemeral, components: response.Components?.Build());
                break;
            case ResponseType.ImageWithEmbed:
                var imageEmbedFilename = StringExtensions.TruncateLongString(StringExtensions.ReplaceInvalidChars(response.FileName), 60);
                await context.Interaction.RespondWithFileAsync(response.Stream,
                    (response.Spoiler
                        ? "SPOILER_"
                        : "") +
                    imageEmbedFilename +
                    ".png",
                    null,
                    new[] { response.Embed?.Build() },
                    ephemeral: ephemeral,
                    components: response.Components?.Build());
                break;
            case ResponseType.Paginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator,
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
    }

    public static async Task SendFollowUpResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
    {
        ulong? responseId = null;
        
        switch (response.ResponseType)
        {
            case ResponseType.Text:
                var text = await context.Interaction.FollowupAsync(response.Text, allowedMentions: AllowedMentions.None, ephemeral: ephemeral, components: response.Components?.Build());
                responseId = text.Id;
                break;
            case ResponseType.Embed:
                var embed = await context.Interaction.FollowupAsync(null, new[] { response.Embed.Build() }, ephemeral: ephemeral, components: response.Components?.Build());
                responseId = embed.Id;
                break;
            case ResponseType.Paginator:
                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator,
                    (SocketInteraction)context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    InteractionResponseType.DeferredChannelMessageWithSource,
                    ephemeral: ephemeral);
                break;
            case ResponseType.ImageWithEmbed:
                var imageEmbedFilename = StringExtensions.TruncateLongString(StringExtensions.ReplaceInvalidChars(response.FileName), 60);
                var imageWithEmbed = await context.Interaction.FollowupWithFileAsync(response.Stream,
                    (response.Spoiler
                        ? "SPOILER_"
                        : "") +
                    imageEmbedFilename +
                    ".png",
                    null,
                    new[] { response.Embed?.Build() },
                    ephemeral: ephemeral,
                    components: response.Components?.Build());

                await response.Stream.DisposeAsync();
                responseId = imageWithEmbed.Id;
                break;
            case ResponseType.ImageOnly:
                var imageName = StringExtensions.TruncateLongString(StringExtensions.ReplaceInvalidChars(response.FileName), 60);
                var image = await context.Interaction.FollowupWithFileAsync(response.Stream,
                (response.Spoiler
                    ? "SPOILER_"
                    : "") +
                imageName +
                ".png",
                null,
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
    }

    public static async Task UpdateInteractionEmbed(this IInteractionContext context, ResponseModel response, InteractiveService interactiveService = null, bool defer = true)
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

    public static async Task DisableInteractionButtons(this IInteractionContext context)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var newComponents = new ComponentBuilder();
        foreach (var actionRowComponent in message.Components)
        {
            foreach (var component in actionRowComponent.Components)
            {
                if (component is ButtonComponent buttonComponent)
                {
                    newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId, buttonComponent.Style,
                        buttonComponent.Emote, buttonComponent.Url, true);
                }
            }
        }

        await message.ModifyAsync(m => m.Components = newComponents.Build());
    }
    
    public static async Task EnableInteractionButtons(this IInteractionContext context)
    {
        var message = (context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var newComponents = new ComponentBuilder();
        foreach (var actionRowComponent in message.Components)
        {
            foreach (var component in actionRowComponent.Components)
            {
                if (component is ButtonComponent buttonComponent)
                {
                    newComponents.WithButton(buttonComponent.Label, buttonComponent.CustomId, buttonComponent.Style,
                        buttonComponent.Emote, buttonComponent.Url);
                }
            }
        }

        await message.ModifyAsync(m => m.Components = newComponents.Build());
    }

    public static async Task UpdateMessageEmbed(this IInteractionContext context, ResponseModel response, string messageId)
    {
        var parsedMessageId = ulong.Parse(messageId);
        var msg = await context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        await context.ModifyMessage(message, response);
    }

    private static async Task ModifyMessage(this IInteractionContext context, IUserMessage message,
        ResponseModel response, bool defer = true)
    {
        await message.ModifyAsync(m =>
        {
            m.Components = response.Components?.Build();
            m.Embed = response.Embed?.Build();
            m.Attachments = response.Stream != null ? new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>
            {
                new(response.Stream, response.Spoiler ? $"SPOILER_{response.FileName}.png" : $"{response.FileName}.png")
            }) : null;
        });

        if (defer)
        {
            await context.Interaction.DeferAsync();
        }
    }

    private static Task ModifyPaginator(this IInteractionContext context, InteractiveService interactiveService, IUserMessage message, ResponseModel response)
    {
        _ = interactiveService.SendPaginatorAsync(
            response.StaticPaginator,
            message,
            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
        return Task.CompletedTask;
    }
}
