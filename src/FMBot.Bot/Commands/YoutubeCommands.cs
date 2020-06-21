using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    public class YoutubeCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFMService _lastFmService;
        private readonly Logger.Logger _logger;
        private readonly UserService _userService;
        private readonly YoutubeService _youtubeService = new YoutubeService();

        private readonly IPrefixService _prefixService;

        public YoutubeCommands(Logger.Logger logger, IPrefixService prefixService, ILastfmApi lastfmApi)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._userService = new UserService();
            this._lastFmService = new LastFMService(lastfmApi);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
        }

        [Command("youtube")]
        [Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("yt", "y", "youtubesearch", "ytsearch", "yts")]
        [LoginRequired]
        public async Task YoutubeAsync(params string[] searchValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                string querystring;
                if (searchValues.Length > 0)
                {
                    querystring = string.Join(" ", searchValues);
                }
                else
                {
                    var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM);

                    if (tracks?.Any() != true)
                    {
                        this._embed.NoScrobblesFoundErrorResponse(tracks.Status, this.Context, this._logger);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    var currentTrack = tracks.Content[0];
                    querystring = currentTrack.Name + " - " + currentTrack.ArtistName;
                }

                try
                {
                    var youtubeResult = this._youtubeService.GetSearchResult(querystring);

                    var name = await this._userService.GetNameAsync(this.Context);

                    var reply = $"{name} searched for: `{querystring}`" +
                                $"\n{youtubeResult.Url}";

                    var rnd = new Random();
                    if (rnd.Next(0, 5) == 1 && searchValues.Length < 1)
                    {
                        reply += "\n*Tip: Search for other songs or videos by simply adding the searchvalue behind .fmyoutube.*";
                    }

                    await ReplyAsync(reply.FilterOutMentions());

                    this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                        this.Context.Message.Content);
                }
                catch (Exception e)
                {
                    this._logger.LogException(this.Context.Message.Content, e);
                    await ReplyAsync("No results have been found for this query.");
                }
            }
            catch (Exception e)
            {
                this._logger.LogException(this.Context.Message.Content, e);
                await ReplyAsync(
                    "Unable to show Last.FM info via YouTube due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
