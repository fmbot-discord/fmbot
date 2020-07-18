using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Microsoft.VisualBasic;
using Constants = FMBot.Bot.Resources.Constants;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace FMBot.Bot.Commands.LastFM
{
    public class AlbumCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly LastFMService _lastFmService;
        private readonly GuildService _guildService;
        private readonly ILastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public AlbumCommands(Logger.Logger logger, ILastfmApi lastfmApi, IPrefixService prefixService)
        {
            this._logger = logger;
            this._lastfmApi = lastfmApi;
            this._lastFmService = new LastFMService(lastfmApi);
            this._prefixService = prefixService;
            this._guildService = new GuildService();
            this._userService = new UserService();
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("album", RunMode = RunMode.Async)]
        [Summary("Displays current album.")]
        [Alias("ab")]
        [LoginRequired]
        public async Task AlbumAsync(params string[] albumValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (albumValues.Any() && albumValues.First() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}album 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the info from the album you're currently listening to.");
                return;
            }

            var searchResult = await this.SearchAlbum(albumValues, userSettings);
            if (!searchResult.AlbumFound)
            {
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
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
            this._embedAuthor.WithName($"Info about {albumInfo.Artist} - {albumInfo.Name} for {userTitle}");
            if (Uri.IsWellFormedUriString(albumInfo.Url, UriKind.Absolute))
            {
                this._embed.WithUrl(albumInfo.Url);
            }
            this._embed.WithAuthor(this._embedAuthor);

            this._embed.AddField("Listeners", albumInfo.Listeners, true);
            this._embed.AddField("Global playcount", albumInfo.Playcount, true);

            if (albumInfo.Userplaycount.HasValue)
            {
                this._embed.AddField("Your playcount", albumInfo.Userplaycount, true);
            }

            if (albumInfo.Image.Any() && albumInfo.Image != null)
            {
                this._embed.WithThumbnailUrl(albumInfo.Image.First(f => f.Size == "mega").Text.ToString());
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

        [Command("albumplays", RunMode = RunMode.Async)]
        [Summary("Displays album plays.")]
        [Alias("abp", "albumplay", "abplays", "albump")]
        [LoginRequired]
        public async Task AlbumPlaysAsync(params string[] albumValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (albumValues.Any() && albumValues.First() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}albumplays 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the plays from the album you're currently listening to.");
                return;
            }

            var searchResult = await this.SearchAlbum(albumValues, userSettings);
            if (!searchResult.AlbumFound)
            {
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
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
            var playstring = albumInfo.Userplaycount == 1 ? "play" : "plays";
            this._embedAuthor.WithName($"{userTitle} has {albumInfo.Userplaycount} {playstring} for {albumInfo.Name} by {albumInfo.Artist}");
            if (Uri.IsWellFormedUriString(albumInfo.Url, UriKind.Absolute))
            {
                this._embed.WithUrl(albumInfo.Url);
            }
            this._embed.WithAuthor(this._embedAuthor);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }


        [Command("cover", RunMode = RunMode.Async)]
        [Summary("Displays current album cover.")]
        [Alias("abc","co", "albumcover")]
        [LoginRequired]
        public async Task AlbumCoverAsync(params string[] albumValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;
            
            var searchResult = await this.SearchAlbum(albumValues, userSettings);
            if (!searchResult.AlbumFound)
            {
                return;
            }

            var albumInfo = await this._lastFmService.GetAlbumImagesAsync(searchResult.Artist, searchResult.Name);

            if (albumInfo.Largest == null)
            {
                this._embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                            $"{searchResult.Artist} - {searchResult.Name}\n" +
                                            $"[View on last.fm]({searchResult.Url})");
                await this.ReplyAsync("", false, this._embed.Build());
                return;
            }

            var image = await this._lastFmService.GetAlbumImageAsBitmapAsync(albumInfo.Largest);
            if (image == null)
            {
                this._embed.WithDescription("Sorry, something went wrong while getting album cover for this album: \n" +
                                            $"{searchResult.Artist} - {searchResult.Name}\n" +
                                            $"[View on last.fm]({searchResult.Url})");
                await this.ReplyAsync("", false, this._embed.Build());
                return;
            }

            this._embed.WithDescription($"**{searchResult.Artist} - [{searchResult.Name}]({searchResult.Url})**");
            this._embedFooter.WithText(
                $"Album cover requested by {await this._userService.GetUserTitleAsync(this.Context)}");
            this._embed.WithFooter(this._embedFooter);

            var imageMemoryStream = new MemoryStream();
            image.Save(imageMemoryStream, ImageFormat.Png);
            imageMemoryStream.Position = 0;

            await this.Context.Channel.SendFileAsync(
                imageMemoryStream,
                $"cover-{StringExtensions.ReplaceInvalidChars($"{searchResult.Artist}_{searchResult.Name}")}.png",
                null,
                false,
                this._embed.Build());

            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        private async Task<AlbumSearchModel> SearchAlbum(string[] albumValues, User userSettings)
        {
            if (albumValues.Any())
            {
                var result = await this._lastFmService.SearchAlbumAsync(string.Join(" ", albumValues));
                if (result.Success && result.Content.Any())
                {
                    var album = result.Content[0];

                    return new AlbumSearchModel(true, album.ArtistName, null, album.Name, album.Url.ToString());
                }
                else if (result.Success)
                {
                    this._embed.WithDescription($"Album could not be found, please check your search values and try again.");
                    await this.ReplyAsync("", false, this._embed.Build());
                    return new AlbumSearchModel(false);
                }
                else
                {
                    this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
                    await this.ReplyAsync("", false, this._embed.Build());
                    return new AlbumSearchModel(false);
                }
            }
            else
            {
                var track = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                if (track == null)
                {
                    this._embed.NoScrobblesFoundErrorResponse(track.Status, this.Context, this._logger);
                    await this.ReplyAsync("", false, this._embed.Build());
                    return new AlbumSearchModel(false);
                }

                var response = track.Content.First();

                return new AlbumSearchModel(true, response.ArtistName, response.ArtistUrl.ToString(), response.AlbumName, response.Url.ToString());
            }
        }

        [Command("topalbums", RunMode = RunMode.Async)]
        [Summary("Displays top albums.")]
        [Alias("abl", "abs", "tab", "albumlist", "albums", "albumslist")]
        [LoginRequired]
        public async Task ArtistsAsync(string time = "weekly", int num = 8, string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (time == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}topalbums '{Constants.CompactTimePeriodList}' 'number of albums (max 12)' 'lastfm username/discord user'`");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            if (num > 12)
            {
                num = 12;
            }
            if (num < 1)
            {
                num = 1;
            }

            var timePeriod = LastFMService.StringToChartTimePeriod(time);
            var timeSpan = LastFMService.ChartTimePeriodToLastStatsTimeSpan(timePeriod);

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                var albums = await this._lastFmService.GetTopAlbumsAsync(lastFMUserName, timeSpan, num);

                if (albums?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(albums.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                string userTitle;
                if (self)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var artistsString = num == 1 ? "album" : "albums";
                this._embedAuthor.WithName($"Top {num} {timePeriod} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{lastFMUserName}/library/albums?date_preset={LastFMService.ChartTimePeriodToSiteTimePeriodUrl(timePeriod)}");
                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < albums.Count(); i++)
                {
                    var album = albums.Content[i];

                    description += $"{i + 1}. {album.ArtistName} - [{album.Name}]({album.Url}) ({album.PlayCount} plays) \n";
                }

                this._embed.WithDescription(description);

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                this._embedFooter.WithText(lastFMUserName + "'s total scrobbles: " +
                                           userInfo.Content.Playcount.ToString("N0"));
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, user);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
