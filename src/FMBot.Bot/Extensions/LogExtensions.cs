using System;
using Discord.Commands;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Extensions
{
    public static class LogExtensions
    {
        public static void LogCommandUsed(this ICommandContext context, CommandResponse commandResponse = CommandResponse.Ok)
        {
            Log.Information("CommandUsed: {userName} / {UserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, context.Message.Content);
        }

        public static void LogCommandException(this ICommandContext context, Exception exception)
        {
            Log.Error(exception, "CommandUsed: {userName} / {UserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, context.Message.Content);
        }
    }
}
