using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.LastFM.Domain.Enums;
using IF.Lastfm.Core.Api.Enums;
using Serilog;

namespace FMBot.Bot.Services
{
    public static class ErrorService
    {
        public static void UsernameNotSetErrorResponse(this EmbedBuilder embed, string prfx)
        {
            embed.WithTitle("Error while attempting get Last.fm information");
            embed.WithDescription("Your Last.fm username has not been set. \n" +
                                        $"Please use the `{prfx}set` command to connect your Last.fm account to .fmbot. \n" +
                                        $"Example: `{prfx}set lastfmusername`\n \n" +
                                        $"For more info, use `{prfx}set help`.");

            embed.WithUrl($"{Constants.DocsUrl}/commands/");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void UserBlockedResponse(this EmbedBuilder embed, string prfx)
        {
            embed.WithTitle("You are not allowed to use .fmbot.");
            embed.WithDescription("You have been blocked from using .fmbot.\n\n" +
                                  "This is probably for a good reason, but if you think this is a mistake you can try contacting us on our support server.\n\n" +
                                  "If you happen to be banned from our support server as well you won't get unblocked. " +
                                  "Maybe host .fmbot yourself since its open-source and maintained by volunteers? " +
                                  "Or consider looking for an alternative.");

            embed.WithThumbnailUrl("https://i.imgur.com/wNmcoR5.jpg");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void SessionRequiredResponse(this EmbedBuilder embed, string prfx)
        {
            embed.WithTitle("Error while attempting get Last.fm information");
            embed.WithDescription("You have not authorized .fmbot yet. \n" +
                                $"Please use the `{prfx}login` command to authorize .fmbot.");

            embed.WithUrl($"{Constants.DocsUrl}/commands/");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, LastResponseStatus? apiResponse, string prfx, string userName)
        {
            embed.WithTitle("Error while attempting get Last.fm information");
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve scrobbles because Last.fm is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                case LastResponseStatus.MissingParameters:
                    embed.WithDescription("You or the user you're searching for has no scrobbles/artists on their profile, or Last.fm is having issues. Please try again later. \n \n" +
                                          $"Recently changed your Last.fm username? Please change it here too using `{prfx}set`. \n" +
                                          $"For more info on your settings, use `{prfx}set help`.");
                    break;
                default:
                    embed.WithDescription(
                        $"The user `{userName}` has no scrobbles/artists/albums/tracks on [their Last.fm profile]({Constants.LastFMUserUrl}{userName}).\n" +
                        $"Just signed up for last.fm and added your account in the bot? Make sure you [track your music](https://www.last.fm/about/trackmymusic) and your Last.fm profile is showing the music that you're listening to.\n\n" +
                        $"Note: this error can also appear when Last.fm is having issues, in that case please try again later. Please note that .fmbot is not affiliated with Last.fm.");
                    break;
            }

            embed.WithThumbnailUrl("https://www.last.fm/static/images/marvin.e51495403de9.png");
            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, string userName)
        {
            embed.WithTitle("Error while attempting get Last.fm information");

            embed.WithDescription($"The user `{userName}` has no scrobbles/artists/albums/tracks on [their Last.fm profile]({Constants.LastFMUserUrl}{userName}).\n" +
                                  $"Just signed up for last.fm and added your account in the bot? Make sure you [track your music](https://www.last.fm/about/trackmymusic), your recent tracks are not marked as private " +
                                  $"and your Last.fm profile is showing the music that you're listening to.\n\n" +
                                  $"Note: this error can also appear when Last.fm is having issues, in that case please try again later. Please note that .fmbot is not affiliated with Last.fm.");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void ErrorResponse(this EmbedBuilder embed, ResponseStatus? apiResponse, string message, ICommandContext context)
        {
            embed.WithTitle("Error while attempting get Last.fm information");
            switch (apiResponse)
            {
                case ResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve data because Last.fm is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                case ResponseStatus.LoginRequired:
                    embed.WithDescription("Can't retrieve data because your recent tracks are marked as private in your [Last.fm privacy settings](https://www.last.fm/settings/privacy).\n" +
                                          "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                default:
                    embed.WithDescription(message);
                    break;
            }

            if (apiResponse != null)
            {
                embed.WithFooter($"Last.fm error code: {apiResponse}");
            }

            embed.WithColor(DiscordConstants.WarningColorOrange);
            Log.Warning($"Last.fm returned error: {message}, error code {apiResponse}", context.Message.Content, context.User.Username, context.Guild?.Name, context.Guild?.Id);
        }
    }
}
