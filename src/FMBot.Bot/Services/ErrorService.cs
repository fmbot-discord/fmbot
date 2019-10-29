using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Bot.Services
{
    public static class ErrorService
    {
        public static EmbedBuilder UsernameNotSetErrorResponse(this EmbedBuilder embed, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset` command. \n" +
                                        $"Usage: `{ConfigData.Data.CommandPrefix}fmset username mode`\n" +
                                        "Possible modes: embedmini/embedfull/textmini/textfull. \n" +
                                        "Please note that users in shared servers will be able to see and request your Last.FM username.");

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError("Last.FM username not set", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);

            return embed;
        }

        public static EmbedBuilder NoScrobblesFoundErrorResponse(this EmbedBuilder embed, LastResponseStatus apiResponse, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    embed.WithDescription("Last.FM has issues and/or is down. Please try again later.");
                    break;
                default:
                    embed.WithDescription(
                        "You have no scrobbles/artists on your profile, or Last.FM is having issues. Please try again later.");
                    break;
            }

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError("No scrobbles found for user", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);

            return embed;
        }
    }
}
