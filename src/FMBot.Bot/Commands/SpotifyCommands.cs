using Discord;
using Discord.Commands;
using Discord.WebSocket;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class SpotifyCommands : ModuleBase
    {
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();
        private readonly SpotifyService _spotifyService = new SpotifyService();
        private readonly LastFMService _lastFmService = new LastFMService();

        public SpotifyCommands(Logger.Logger logger)
        {
            _logger = logger;
        }

        [Command("fmspotify"), Summary("Shares a link to a Spotify track based on what a user is listening to")]
        public async Task fmspotifyAsync(IUser user = null)
        {
            Data.Entities.User userSettings = await _userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            try
            {
                PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);
                LastTrack currentTrack = tracks.Content[0];

                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                string querystring = TrackName + " - " + ArtistName + " " + AlbumName;

                SearchItem item = await _spotifyService.GetSearchResultAsync(querystring);

                if (item.Tracks?.Items?.Any() == true)
                {
                    FullTrack track = item.Tracks.Items.FirstOrDefault();
                    SimpleArtist trackArtist = track.Artists.FirstOrDefault();

                    await ReplyAsync("https://open.spotify.com/track/" + track.Id);
                    this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
                }
                else
                {
                    await ReplyAsync("No results have been found for this track. Querystring: `" + querystring + "`");
                }
            }
            catch (Exception e)
            {
                _logger.LogException(Context.Message.Content, e);
                await ReplyAsync("Unable to show Last.FM info via Spotify due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmspotifysearch"), Summary("Shares a link to a Spotify track based on a user's search parameters")]
        [Alias("fmspotifyfind")]
        public async Task fmspotifysearchAsync(params string[] searchterms)
        {
            try
            {
                string querystring = null;

                if (searchterms.Length > 0)
                {
                    querystring = string.Join(" ", searchterms);

                    SearchItem item = await _spotifyService.GetSearchResultAsync(querystring);

                    if (item.Tracks.Items.Count > 0)
                    {
                        FullTrack track = item.Tracks.Items.FirstOrDefault();
                        SimpleArtist trackArtist = track.Artists.FirstOrDefault();

                        await ReplyAsync("https://open.spotify.com/track/" + track.Id);
                        this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);

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
                _logger.LogException(Context.Message.Content, e);

                await ReplyAsync("Unable to search for music via Spotify due to an internal error.");
            }
        }
    }
}
