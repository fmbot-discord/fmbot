using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using IF.Lastfm.Core.Api.Enums;
using Serilog;

namespace FMBot.Bot.Services
{
    public static class ErrorService
    {
        public static void UsernameNotSetErrorResponse(this EmbedBuilder embed, string prfx, string name)
        {
            embed.WithDescription($"Hi {name}, welcome to .fmbot. \n" +
                                  $"To use this bot you first need to add your Last.fm account.\n\n" +
                                  $"Please use the `{prfx}login` command. The bot will then DM you a link so you can connect your Last.fm account.");

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
            embed.WithDescription("While you have set your username, you haven't connected .fmbot to your Last.fm account yet, which is required for the command you're trying to use.\n" +
                                $"Please use the `{prfx}login` command to receive a link to connect your Last.fm account.");

            embed.WithUrl($"{Constants.DocsUrl}/commands/");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, LastResponseStatus? apiResponse, string prfx, string userName)
        {
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    embed.WithTitle("Error while attempting get Last.fm information");
                    embed.WithDescription(
                        "Can't retrieve scrobbles because Last.fm is having issues. Please try again later. \n" +
                        "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                case LastResponseStatus.MissingParameters:
                    embed.WithTitle("Error while attempting get Last.fm information");
                    embed.WithDescription(
                        "You or the user you're searching for has no scrobbles/artists on their profile, or Last.fm is having issues. Please try again later. \n \n" +
                        $"Recently changed your Last.fm username? Please change it here too using `{prfx}login` again.");
                    break;
                default:
                    embed.WithDescription(
                        $"The Last.fm user `{userName}` has no scrobbles/artists/albums/tracks on [their profile]({Constants.LastFMUserUrl}{userName}).\n\n" +
                        $"Just signed up for last.fm and added your account in the bot? Make sure you [track your music](https://www.last.fm/about/trackmymusic) and your Last.fm profile is showing the music that you're listening to.");
                    break;
            }

            embed.WithThumbnailUrl("https://www.last.fm/static/images/marvin.e51495403de9.png");
            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        private static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, string userName)
        {
            embed.WithTitle("Error while attempting get Last.fm information");

            embed.WithDescription($"The Last.fm user `{userName}` has no scrobbles/artists/albums/tracks on [their Last.fm profile]({Constants.LastFMUserUrl}{userName}).\n\n" +
                                  $"Just signed up for last.fm and added your account in the bot? Make sure you properly [track your plays](https://www.last.fm/about/trackmymusic) " +
                                  $"and your [Last.fm profile]({Constants.LastFMUserUrl}{userName}) is showing the music that you're listening to.");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public static void ErrorResponse(this EmbedBuilder embed, ResponseStatus? responseStatus, string message, ICommandContext context, string expectedResultType = null)
        {
            embed.WithTitle("Error while attempting get Last.fm information");
            switch (responseStatus)
            {
                case ResponseStatus.Failure:
                    embed.WithDescription("Can't retrieve data because Last.fm is having issues. Please try again later. \n" +
                                          "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                case ResponseStatus.LoginRequired:
                    embed.WithDescription("Can't retrieve data because your recent tracks are marked as private in your [Last.fm privacy settings](https://www.last.fm/settings/privacy).\n\n" +
                                          "You can either change this setting or authorize .fmbot to access your private scrobbles with `.fmlogin`.\n\n" +
                                          "Please note that .fmbot isn't affiliated with Last.fm.");
                    break;
                case ResponseStatus.BadAuth:
                    embed.WithDescription("Can't retrieve data because your Last.fm session is expired, invalid or Last.fm is having issues.\n" +
                                          "Please try a re-login to the bot with `.fmlogin`.");
                    break;
                case ResponseStatus.SessionExpired:
                    embed.WithDescription("Can't retrieve data because your Last.fm session is expired or invalid.\n" +
                                          "Please re-login to the bot with `.fmlogin`.");
                    break;
                case ResponseStatus.MissingParameters:
                    if (expectedResultType != null)
                    {
                        embed.Title = null;
                        embed.WithDescription($"Sorry, Last.fm did not return an {expectedResultType} for the name you searched for.");
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                default:
                    embed.WithDescription(message);
                    break;
            }

            if (responseStatus != null)
            {
                embed.WithFooter($"Last.fm error code: {responseStatus}");
            }

            embed.WithColor(DiscordConstants.WarningColorOrange);
            Log.Information("Last.fm returned error: {message} | {responseStatus} | {discordUserName} / {discordUserId} | {messageContent}", message, responseStatus, context.User.Username, context.User.Id, context.Message.Content);
        }

        public static bool RecentScrobbleCallFailed(Response<RecentTrackList> recentScrobbles, string lastFmUserName)
        {
            if (!recentScrobbles.Success || recentScrobbles.Content == null || !recentScrobbles.Content.RecentTracks.Any())
            {
                return true;
            }

            return false;
        }

        public static async Task<bool> RecentScrobbleCallFailedReply(Response<RecentTrackList> recentScrobbles, string lastFmUserName, ICommandContext context)
        {
            var embed = new EmbedBuilder();
            if (!recentScrobbles.Success || recentScrobbles.Content == null)
            {
                embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, context);
                context.LogCommandUsed(CommandResponse.LastFmError);

                await context.Channel.SendMessageAsync("", false, embed.Build());

                return true;
            }

            if (!recentScrobbles.Content.RecentTracks.Any())
            {
                embed.NoScrobblesFoundErrorResponse(lastFmUserName);
                context.LogCommandUsed(CommandResponse.NoScrobbles);
                await context.Channel.SendMessageAsync("", false, embed.Build());
                return true;
            }

            return false;
        }
    }
}
