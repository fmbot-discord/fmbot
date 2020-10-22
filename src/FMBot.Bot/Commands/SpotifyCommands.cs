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
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    public class SpotifyCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFMService _lastFmService;
        private readonly SpotifyService _spotifyService;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public SpotifyCommands(
            IPrefixService prefixService,
            LastFMService lastFmService,
            UserService userService,
            SpotifyService spotifyService)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._spotifyService = spotifyService;
            this._lastFmService = lastFmService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
        }

        [Command("spotify")]
        [Summary("Shares a link to a Spotify track based on what a user is listening to")]
        [Alias("sp", "s", "spotifyfind", "spotifysearch")]
        [UsernameSetRequired]
        public async Task SpotifyAsync(params string[] searchValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

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
                    var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                    if (tracks?.Any() != true)
                    {
                        this._embed.NoScrobblesFoundErrorResponse(tracks.Status, prfx, userSettings.UserNameLastFM);
                        this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    var currentTrack = tracks.Content[0];

                    var trackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                    var artistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                    var albumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                    querystring = $"{trackName} {artistName} {albumName}";
                }

                var item = await this._spotifyService.GetSearchResultAsync(querystring);

                if (item.Tracks?.Items?.Any() == true)
                {
                    var track = item.Tracks.Items.FirstOrDefault();
                    var reply = $"https://open.spotify.com/track/{track.Id}";

                    var rnd = new Random();
                    if (rnd.Next(0, 5) == 1 && searchValues.Length < 1)
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
                    "Unable to show Last.FM info via Spotify due to an internal error. " +
                    "Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
