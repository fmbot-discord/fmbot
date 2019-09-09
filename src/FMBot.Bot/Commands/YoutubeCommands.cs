using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Services;
using IF.Lastfm.Core.Objects;
using System;
using System.Threading.Tasks;
using YoutubeSearch;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class YoutubeCommands : ModuleBase
    {
        private readonly LastFMService lastFMService = new LastFMService();

        private readonly UserService userService = new UserService();

        private readonly YoutubeService youtubeService = new YoutubeService();

        [Command("fmyoutube"), Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("fmyt")]
        public async Task fmytAsync(IUser user = null)
        {
            Data.Entities.User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.").ConfigureAwait(false);
                return;
            }

            try
            {
                LastTrack track = await lastFMService.GetLastScrobbleAsync(userSettings.UserNameLastFM).ConfigureAwait(false);

                if (track == null)
                {
                    await ReplyAsync("No scrobbles found on your LastFM profile. (" + userSettings.UserNameLastFM + ")").ConfigureAwait(false);
                    return;
                }

                try
                {
                    string querystring = track.Name + " - " + track.ArtistName;

                    VideoInformation youtubeResult = youtubeService.GetSearchResult(querystring);

                    await ReplyAsync($"Searched for: `{querystring}`\n " +
                        youtubeResult.Url).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(disclient, e);
                    await ReplyAsync("No results have been found for this track.").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info via YouTube due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.").ConfigureAwait(false);
            }
        }


        [Command("fmyoutubesearch"), Summary("Search for a youtube video")]
        [Alias("fmytsearch")]
        public async Task fmytSearchAsync(params string[] searchterms)
        {
            if (searchterms.Length < 1)
            {
                await ReplyAsync("Please enter a searchvalue.").ConfigureAwait(false);
                return;
            }

            string querystring = string.Join(" ", searchterms);

            try
            {
                VideoInformation youtubeResult = youtubeService.GetSearchResult(querystring);

                await ReplyAsync(youtubeResult.Url).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("No results have been found for this track.").ConfigureAwait(false);
            }
        }
    }
}
