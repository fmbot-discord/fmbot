using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Services;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeSearch;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class YoutubeCommands : ModuleBase
    {
        private LastFMService lastFMService = new LastFMService();

        private UserService userService = new UserService();

        private YoutubeService youtubeService = new YoutubeService();

        [Command("fmyt"), Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("fmyoutube")]
        public async Task fmytAsync(IUser user = null)
        {
            Data.Entities.User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            try
            {
                LastTrack track = await lastFMService.GetLastScrobbleAsync(userSettings.UserNameLastFM);

                if (track == null)
                {
                    await ReplyAsync("No scrobbles found on your LastFM profile. (" + userSettings.UserNameLastFM + ")");
                    return;
                }

                try
                {
                    string querystring = track.Name + " - " + track.ArtistName + " " + track.AlbumName;

                    VideoInformation youtubeResult = youtubeService.GetSearchResult(querystring);

                    await ReplyAsync(youtubeResult.Url);
                }
                catch (Exception e)
                {
                    DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(disclient, e);
                    await ReplyAsync("No results have been found for this track.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info via YouTube due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

    }
}
