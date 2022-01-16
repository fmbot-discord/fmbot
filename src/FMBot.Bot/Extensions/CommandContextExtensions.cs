using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using Serilog;

namespace FMBot.Bot.Extensions
{
    public static class CommandContextExtensions
    {
        public static void LogCommandUsed(this ICommandContext context, CommandResponse commandResponse = CommandResponse.Ok)
        {
            Log.Information("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, context.Message.Content);
        }

        public static void LogCommandException(this ICommandContext context, Exception exception, string message = null)
        {
            Log.Error(exception, "CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, context.Message.Content);
        }

        public static void LogCommandWithLastFmError(this ICommandContext context, ResponseStatus? responseStatus)
        {
            Log.Error("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.LastFmError, context.Message.Content, responseStatus);
        }

        public static async Task SendResponse(this ICommandContext context, InteractiveService interactiveService, ResponseModel response)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Channel.SendMessageAsync(response.Text, allowedMentions: AllowedMentions.None);
                    break;
                case ResponseType.Embed:
                    await context.Channel.SendMessageAsync("", false, response.Embed.Build());
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator,
                        context.Channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
