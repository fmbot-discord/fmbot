using FMBot.Domain;
using NetCord.Services.Commands;
using Shared.Domain.Enums;

namespace FMBot.Bot.Extensions;

public static class BotTypeExtension
{
    public static BotType GetBotType(this CommandContext context)
    {
        var botId = context.Client.Id;

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
