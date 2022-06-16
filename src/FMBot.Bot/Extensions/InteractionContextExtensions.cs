using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using Serilog;

namespace FMBot.Bot.Extensions
{
    public static class InteractionContextExtensions
    {
        public static void LogCommandUsed(this IInteractionContext context, CommandResponse commandResponse = CommandResponse.Ok)
        {
            string commandName = null;
            if (context.Interaction is SocketSlashCommand socketSlashCommand)
            {
                commandName = socketSlashCommand.CommandName;
            }

            Log.Information("SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, commandName);
        }

        public static void LogCommandException(this IInteractionContext context, Exception exception, string message = null)
        {
            string commandName = null;
            if (context.Interaction is SocketSlashCommand socketSlashCommand)
            {
                commandName = socketSlashCommand.CommandName;
            }

            Log.Error(exception, "SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, commandName);
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
        }

        public static async Task SendResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Interaction.RespondAsync(response.Text, allowedMentions: AllowedMentions.None, ephemeral: ephemeral);
                    break;
                case ResponseType.Embed:
                    await context.Interaction.RespondAsync(null, new[] { response.Embed.Build() }, ephemeral: ephemeral);
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator,
                        (SocketInteraction)context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        ephemeral: ephemeral);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static async Task SendFollowUpResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Interaction.FollowupAsync(response.Text, allowedMentions: AllowedMentions.None, ephemeral: ephemeral);
                    break;
                case ResponseType.Embed:
                    await context.Interaction.FollowupAsync(null, new[] { response.Embed.Build() }, ephemeral: ephemeral);
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
                    await context.Interaction.FollowupWithFileAsync(response.Stream,
                        (response.Spoiler
                            ? "SPOILER_"
                            : "") +
                        response.FileName +
                        ".png",
                        null,
                        new[] { response.Embed.Build() },
                        ephemeral: ephemeral);
                    await response.Stream.DisposeAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
