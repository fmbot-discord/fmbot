using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands
{
    public class GeniusCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly GeniusService _geniusService = new GeniusService();

        private readonly UserService _userService = new UserService();

        public GeniusCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
        }

        [Command("fmgenius")]
        [Summary("Shares a link to the Genius lyrics based on what a user is listening to")]
        [Alias("fmlyrics")]
        public async Task GeniusAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            try
            {
                var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                if (tracks?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(tracks.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                var currentTrack = tracks.Content[0];
                var querystring = $"{currentTrack.ArtistName} {currentTrack.Name}";

                var url = await this._geniusService.GetUrlAsync(querystring);

                if (url != null)
                {
                    await ReplyAsync($"<{url}> \n" +
                                     "Not quite right? Use `.fmgeniussearch` to further refine your search.");
                    this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                        this.Context.Message.Content);
                }
                else
                {
                    await ReplyAsync("No results have been found for this track. Querystring: `" + querystring + "` \n" +
                                     "Most likely the song isn't on Genius yet.'");
                }
            }
            catch (Exception e)
            {
                this._logger.LogException(this.Context.Message.Content, e);
                await ReplyAsync(
                    "Unable to show Last.FM info via Genius due to an internal error. " +
                    "Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmgeniussearch")]
        [Summary("Shares a link to the Genius lyrics based on a user's search parameters")]
        [Alias("fmgeniusfind", "fmlyricfind", "fmlyricsfind", "fmlyricsearch", "fmlyricssearch")]
        public async Task GeniusSearchAsync(params string[] searchValues)
        {
            try
            {
                if (searchValues.Length > 0)
                {
                    var querystring = string.Join(" ", searchValues);

                    var url = await this._geniusService.GetUrlAsync(querystring);

                    if (url != null)
                    {
                        await ReplyAsync($"<{url}>");
                        this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id,
                            this.Context.User.Id, this.Context.Message.Content);
                    }
                    else
                    {
                        await ReplyAsync("No results have been found for this track.");
                    }
                }
                else
                {
                    await ReplyAsync("Please specify what you want to search for.");
                }
            }
            catch (Exception e)
            {
                this._logger.LogException(this.Context.Message.Content, e);

                await ReplyAsync("Unable to search for music via Genius due to an internal error.");
            }
        }
    }
}
