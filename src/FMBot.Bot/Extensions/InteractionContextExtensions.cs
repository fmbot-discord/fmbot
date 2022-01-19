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
            Log.Information("SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, context.Interaction.ToString());
        }

        public static void LogCommandException(this IInteractionContext context, Exception exception, string message = null)
        {
            Log.Error(exception, "SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, context.Interaction.ToString());
        }

        public static void LogCommandWithLastFmError(this IInteractionContext context, ResponseStatus? responseStatus)
        {
            Log.Error("SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.LastFmError, context.Interaction.ToString());
        }

        public static async Task SendResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Interaction.RespondAsync(response.Text, allowedMentions: AllowedMentions.None);
                    break;
                case ResponseType.Embed:
                    await context.Interaction.RespondAsync(null, new[] { response.Embed.Build() });
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator,
                        (SocketInteraction)context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static async Task SendFollowUpResponse(this IInteractionContext context, InteractiveService interactiveService, ResponseModel response)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Interaction.FollowupAsync(response.Text, allowedMentions: AllowedMentions.None);
                    break;
                case ResponseType.Embed:
                    await context.Interaction.FollowupAsync(null, new[] { response.Embed.Build() });
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator,
                        (SocketInteraction)context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        InteractionResponseType.DeferredChannelMessageWithSource);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
