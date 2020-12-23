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
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    [Name("Spotify")]
    public class SpotifyCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFmService _lastFmService;
        private readonly SpotifyService _spotifyService;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public SpotifyCommands(
            IPrefixService prefixService,
            LastFmService lastFmService,
            UserService userService,
            SpotifyService spotifyService)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._spotifyService = spotifyService;
            this._lastFmService = lastFmService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
        }

        [Command("spotify")]
        [Summary("Shares a link to a Spotify track based on what a user is listening to")]
        [Alias("sp", "s", "spotifyfind", "spotifysearch")]
        [UsernameSetRequired]
        public async Task SpotifyAsync([Remainder] string searchValue = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                string querystring;
                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    querystring = searchValue;
                }
                else
                {
                    string sessionKey = null;
                    if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                    {
                        sessionKey = userSettings.SessionKeyLastFm;
                    }

                    var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                    if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    var currentTrack = recentScrobbles.Content.RecentTracks.Track[0];

                    var albumName = string.IsNullOrWhiteSpace(currentTrack.Album?.Text) ? null : currentTrack.Album.Text;

                    querystring = $"{currentTrack.Name} {currentTrack.Artist.Text} {albumName}";
                }

                var item = await this._spotifyService.GetSearchResultAsync(querystring);

                if (item.Tracks?.Items?.Any() == true)
                {
                    var track = item.Tracks.Items.FirstOrDefault();
                    var reply = $"https://open.spotify.com/track/{track.Id}";

                    var rnd = new Random();
                    if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                    {
                        reply += $"\n*Tip: Search for other songs by simply adding the searchvalue behind {prfx}spotify.*";
                    }

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed();
                }
                else
                {
                    await ReplyAsync("No results have been found for this track. Querystring: `" + querystring.FilterOutMentions() + "`");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                }
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show Last.fm info via Spotify due to an internal error. " +
                    " Please try again later or contact .fmbot support.");
            }
        }
    }
}
