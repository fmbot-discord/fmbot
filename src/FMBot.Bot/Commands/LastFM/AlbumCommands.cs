using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.ApiModels;
using FMBot.LastFM.Services;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace FMBot.Bot.Commands.LastFM
{
    public class AlbumCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly ILastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public AlbumCommands(Logger.Logger logger, ILastfmApi lastfmApi)
        {
            this._logger = logger;
            this._lastfmApi = lastfmApi;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmalbum", RunMode = RunMode.Async)]
        [Summary("Displays current album.")]
        [Alias("fmab")]
        public async Task AlbumAsync()
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
                var tags = this._lastFmService.TagsToLinkedString(albumInfo.Tags);

                this._embed.AddField("Tags", tags);
            }

            var description = "";

            this._embed.WithDescription(description);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        [Command("fmcover", RunMode = RunMode.Async)]
        [Summary("Displays current album cover.")]
        [Alias("fmabc","fmco", "fmalbumcover")]
        public async Task AlbumCoverAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

            if (tracks?.Any() != true)
            {
                this._embed.NoScrobblesFoundErrorResponse(tracks.Status, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var currentTrack = tracks.Content[0];
            var albumInfo = await this._lastFmService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

            if (albumInfo.Largest == null)
            {
                this._embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                            $"{currentTrack.ArtistName} - {currentTrack.AlbumName}\n" +
                                            $"[View on last.fm]({currentTrack.Url})");
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var image = await this._lastFmService.GetAlbumImageAsBitmapAsync(albumInfo.Largest);
            if (image == null)
            {
                this._embed.WithDescription("Sorry, something went wrong while getting album cover for this album: \n" +
                                            $"{currentTrack.ArtistName} - {currentTrack.AlbumName}\n" +
                                            $"[View on last.fm]({currentTrack.Url})");
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            this._embed.WithDescription(LastFMService.TrackToLinkedString(currentTrack));

            this._embedFooter.WithText(
                $"Album cover requested by {await this._userService.GetUserTitleAsync(this.Context)}");
            this._embed.WithFooter(this._embedFooter);

            var imageMemoryStream = new MemoryStream();
            image.Save(imageMemoryStream, ImageFormat.Png);
            imageMemoryStream.Position = 0;

            await this.Context.Channel.SendFileAsync(
                imageMemoryStream,
                $"cover-{currentTrack.Mbid}.png",
                null,
                false,
                this._embed.Build());

            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }
    }
}
