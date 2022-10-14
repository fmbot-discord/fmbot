using Discord.Commands;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Extensions;

public static class BotTypeExtension
{
    public static BotType GetBotType(this ICommandContext context)
    {
        var socketCommandContext = (SocketCommandContext)context;
        var botId = socketCommandContext.Client.CurrentUser.Id;

        return botId switch
        {
            Constants.BotProductionId => BotType.Production,
            Constants.BotBetaId => BotType.Beta,
            _ => BotType.Local
        };
    }
        
    public static BotType GetBotType(ulong botId)
    {
        return botId switch
        {
            Constants.BotProductionId => BotType.Production,
            Constants.BotBetaId => BotType.Beta,
            _ => BotType.Local
        };
    }
}
