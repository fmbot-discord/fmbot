using System;
using Discord.Commands;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using Serilog;

namespace FMBot.Bot.Extensions
{
    public static class LogExtensions
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
    }
}
