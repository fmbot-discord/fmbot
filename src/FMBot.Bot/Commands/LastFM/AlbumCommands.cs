using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.LastFM.Models;
using FMBot.LastFM.Services;
using SpotifyAPI.Web.Models;

namespace FMBot.Bot.Commands.LastFM
{
    public class AlbumCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly LastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public AlbumCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._lastfmApi = new LastfmApi(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmalbum", RunMode = RunMode.Async)]
        [Summary("Displays current album.")]
        [Alias("fmab")]
        public async Task AlbumsAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var track = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

            if (track == null)
            {
                this._embed.NoScrobblesFoundErrorResponse(track.Status, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", track.Content.First().ArtistName },
                {"album", track.Content.First().AlbumName },
                {"username", userSettings.UserNameLastFM }
            };

            var albumCall = await this._lastfmApi.CallApiAsync<AlbumResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error.Value, albumCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var albumInfo = albumCall.Content.Album;

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Album info about {albumInfo.Name} for {userTitle}");
            this._embedAuthor.WithUrl(albumInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            this._embed.AddField("Listeners", albumInfo.Listeners, true);
            this._embed.AddField("Global playcount", albumInfo.Playcount, true);
            if (albumInfo.Userplaycount.HasValue)
            {
                this._embed.AddField("Your playcount", albumInfo.Userplaycount, true);
            }

            if (!string.IsNullOrWhiteSpace(albumInfo.Wiki?.Summary))
            {
                var linktext = $"<a href=\"{albumInfo.Url.Replace("https", "http")}\">Read more on Last.fm</a>";
                this._embed.AddField("Summary", albumInfo.Wiki.Summary.Replace(linktext, ""));
            }

            if (albumInfo.Tags.Tag.Any())
            {
                string tags = "";
                for (var i = 0; i < albumInfo.Tags.Tag.Length; i++)
                {
                    if (i != 0)
                    {
                        tags += " - ";
                    }
                    var tag = albumInfo.Tags.Tag[i];
                    tags += $"[{tag.Name}]({tag.Url})";
                }

                this._embed.AddField("Tags", tags);
            }

            var description = "";

            this._embed.WithDescription(description);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }
    }
}
