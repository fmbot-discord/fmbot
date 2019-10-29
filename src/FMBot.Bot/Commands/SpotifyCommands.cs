using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands
{
    public class SpotifyCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly SpotifyService _spotifyService = new SpotifyService();

        private readonly UserService _userService = new UserService();

        public SpotifyCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
        }

        [Command("fmspotify")]
        [Summary("Shares a link to a Spotify track based on what a user is listening to")]
        public async Task SpotifyAsync()
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

                var trackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                var artistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                var albumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                var querystring = trackName + " - " + artistName + " " + albumName;

                var item = await this._spotifyService.GetSearchResultAsync(querystring);

                if (item.Tracks?.Items?.Any() == true)
                {
                    var track = item.Tracks.Items.FirstOrDefault();

                    await ReplyAsync("https://open.spotify.com/track/" + track.Id);
                    this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                        this.Context.Message.Content);
                }
                else
                {
                    await ReplyAsync("No results have been found for this track. Querystring: `" + querystring + "`");
                }
            }
            catch (Exception e)
            {
                this._logger.LogException(this.Context.Message.Content, e);
                await ReplyAsync(
                    "Unable to show Last.FM info via Spotify due to an internal error. " +
                    "Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmspotifysearch")]
        [Summary("Shares a link to a Spotify track based on a user's search parameters")]
        [Alias("fmspotifyfind")]
        public async Task SpotifySearchAsync(params string[] searchValues)
        {
            try
            {
                if (searchValues.Length > 0)
                {
                    var querystring = string.Join(" ", searchValues);

                    var item = await this._spotifyService.GetSearchResultAsync(querystring);

                    if (item.Tracks.Items.Count > 0)
                    {
                        var track = item.Tracks.Items.FirstOrDefault();

                        await ReplyAsync("https://open.spotify.com/track/" + track.Id);
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

                await ReplyAsync("Unable to search for music via Spotify due to an internal error.");
            }
        }
    }
}
