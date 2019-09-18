using Discord;
using Discord.Commands;
using Discord.WebSocket;
using IF.Lastfm.Core.Objects;
using System;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using FMBot.YoutubeSearch;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class YoutubeCommands : ModuleBase
    {
        private readonly Logger.Logger _logger;

        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly UserService _userService = new UserService();
        private readonly YoutubeService _youtubeService = new YoutubeService();

        public YoutubeCommands(Logger.Logger logger)
        {
            _logger = logger;
        }

        [Command("fmyoutube"), Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("fmyt")]
        public async Task fmytAsync(IUser user = null)
        {
            Data.Entities.User userSettings = await _userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.").ConfigureAwait(false);
                return;
            }

            try
            {
                LastTrack track = await _lastFmService.GetLastScrobbleAsync(userSettings.UserNameLastFM).ConfigureAwait(false);

                if (track == null)
                {
                    await ReplyAsync("No scrobbles found on your LastFM profile. (" + userSettings.UserNameLastFM + ")").ConfigureAwait(false);
                    return;
                }

                try
                {
                    string querystring = track.Name + " - " + track.ArtistName;

                    VideoInformation youtubeResult = _youtubeService.GetSearchResult(querystring);

                    await ReplyAsync($"Searched for: `{querystring}`\n " +
                        youtubeResult.Url).ConfigureAwait(false);

                    this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
                }
                catch (Exception e)
                {
                    _logger.LogException(Context.Message.Content, e);
                    await ReplyAsync("No results have been found for this track.").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogException(Context.Message.Content, e);
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
                VideoInformation youtubeResult = _youtubeService.GetSearchResult(querystring);

                await ReplyAsync(youtubeResult.Url).ConfigureAwait(false);
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogException(Context.Message.Content, e);
                await ReplyAsync("No results have been found for this track.").ConfigureAwait(false);
            }
        }
    }
}
