using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.LastFM.Models;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Bot.Services
{
    public static class ErrorService
    {
        public static void UsernameNotSetErrorResponse(this EmbedBuilder embed, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset` command. \n" +
                                        $"Example: `{ConfigData.Data.CommandPrefix}fmset lastfmusername`\n \n" +
                                        $"For more info, use `.fmset help`.");

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError("Last.FM username not set", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);
        }

        public static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, LastResponseStatus apiResponse, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve scrobbles because Last.FM is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.FM.");
                    break;
                case LastResponseStatus.MissingParameters:
                    embed.WithDescription("You or the user you're searching for has no scrobbles/artists on their profile, or Last.FM is having issues. Please try again later. \n \n" +
                                          "Recently changed your Last.FM username? Please change it here too using `.fmset`. \n" +
                                          "For more info on your settings, use `.fmset help`.");
                    break;
                default:
                    embed.WithDescription(
                        "You or the user you're searching for has no scrobbles/artists on their profile, or Last.FM is having issues. Please try again later.");
                    break;
            }

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError($"No scrobbles found for user, error code {apiResponse}", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);
        }

        public static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, ResponseStatus apiResponse, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case ResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve scrobbles because Last.FM is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.FM.");
                    break;
                default:
                    embed.WithDescription(
                        "You have no scrobbles/artists on your profile, or Last.FM is having issues. Please try again later.");
                    break;
            }

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError($"No scrobbles found for user, error code {apiResponse}", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);
        }

        public static void ErrorResponse(this EmbedBuilder embed, ResponseStatus apiResponse, string message, ICommandContext context, Logger.Logger logger)
        {
            embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case ResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve scrobbles because Last.FM is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.FM.");
                    break;
                default:
                    embed.WithDescription(
                        message);
                    break;
            }

            embed.WithColor(Constants.WarningColorOrange);
            logger.LogError($"Last.fm returned error: {message}, error code {apiResponse}", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);
        }
    }
}
