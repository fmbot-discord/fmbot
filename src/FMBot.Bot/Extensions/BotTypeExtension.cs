using Discord.Commands;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Extensions
{
    public static class BotTypeExtension
    {
        public static BotType GetBotType(this ICommandContext context)
        {
            var botId = context.Client.CurrentUser.Id;

            return botId switch
            {
                Constants.BotProductionId => BotType.Production,
                Constants.BotDevelopId => BotType.Develop,
                _ => BotType.Local
            };
        }
        
        public static BotType GetBotType(ulong botId)
        {
            return botId switch
            {
                Constants.BotProductionId => BotType.Production,
                Constants.BotDevelopId => BotType.Develop,
                _ => BotType.Local
            };
        }
    }
}
