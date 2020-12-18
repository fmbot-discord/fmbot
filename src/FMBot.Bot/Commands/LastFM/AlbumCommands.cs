using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.API.Rest;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Constants = FMBot.Domain.Constants;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Albums")]
    public class AlbumCommands : ModuleBase
    {
        private readonly CensorService _censorService;
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly ILastfmApi _lastFmApi;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmService _lastFmService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly UserService _userService;
        private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public AlbumCommands(
                CensorService censorService,
                GuildService guildService,
                IIndexService indexService,
                ILastfmApi lastFmApi,
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmService lastFmService,
                PlayService playService,
                SettingService settingService,
                UserService userService,
                WhoKnowsAlbumService whoKnowsAlbumService,
                WhoKnowsPlayService whoKnowsPlayService)
        {
            this._censorService = censorService;
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmService = lastFmService;
            this._lastFmApi = lastFmApi;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowsAlbumService = whoKnowsAlbumService;
            this._whoKnowsPlayService = whoKnowsPlayService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("album", RunMode = RunMode.Async)]
        [Summary("Displays current album.")]
        [Alias("ab")]
        [UsernameSetRequired]
        public async Task AlbumAsync([Remainder] string albumValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.ToLower() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}album 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the info from the album you're currently listening to.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var searchResult = await this.SearchAlbum(albumValues, userSettings, prfx);
            if (!searchResult.AlbumFound)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
                {"username", userSettings.UserNameLastFM }
            };

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
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
                var filteredSummary = albumInfo.Wiki.Summary.Replace(linktext, "");
                if (!string.IsNullOrWhiteSpace(filteredSummary))
                {
                    this._embed.AddField("Summary", filteredSummary);
                }
            }

            if (albumInfo.Tags.Tag.Any())
            {
                var tags = LastFmService.TagsToLinkedString(albumInfo.Tags);

                this._embed.AddField("Tags", tags);
            }

            var description = "";

            this._embed.WithDescription(description);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("albumplays", RunMode = RunMode.Async)]
        [Summary("Displays album plays.")]
        [Alias("abp", "albumplay", "abplays", "albump", "album plays")]
        [UsernameSetRequired]
        public async Task AlbumPlaysAsync([Remainder] string albumValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.ToLower() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}albumplays 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the plays from the album you're currently listening to.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var searchResult = await this.SearchAlbum(albumValues, userSettings, prfx);
            if (!searchResult.AlbumFound)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
                {"username", userSettings.UserNameLastFM }
            };

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
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
            this.Context.LogCommandUsed();
        }


        [Command("cover", RunMode = RunMode.Async)]
        [Summary("Displays current album cover.")]
        [Alias("abc", "co", "albumcover", "album cover")]
        [UsernameSetRequired]
        public async Task AlbumCoverAsync([Remainder] string albumValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.ToLower() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}cover 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the cover from the album you're currently listening to.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var searchResult = await this.SearchAlbum(albumValues, userSettings, prfx);
            if (!searchResult.AlbumFound)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var albumInfo = await this._lastFmService.GetAlbumImagesAsync(searchResult.Artist, searchResult.Name);

            if (albumInfo == null || albumInfo.Largest == null)
            {
                this._embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                            $"{searchResult.Artist} - {searchResult.Name}\n" +
                                            $"[View on last.fm]({searchResult.Url})");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var image = await LastFmService.GetAlbumImageAsBitmapAsync(albumInfo.Largest);
            if (image == null)
            {
                this._embed.WithDescription("Sorry, something went wrong while getting album cover for this album: \n" +
                                            $"{searchResult.Artist} - {searchResult.Name}\n" +
                                            $"[View on last.fm]({searchResult.Url})");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            if (!await this._censorService.AlbumIsSafe(searchResult.Name, searchResult.Artist))
            {
                this._embed.WithDescription("Sorry, this album or artist is filtered due to the nsfw image.\n" +
                                            "The ability to disable this error for nsfw channels will be added soon.");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Censored);
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
            this.Context.LogCommandUsed();
        }

        private async Task<AlbumSearchModel> SearchAlbum(string albumValues, User userSettings, string prfx)
        {
            if (!string.IsNullOrWhiteSpace(albumValues))
            {
                var searchValue = albumValues;

                if (searchValue.Contains(" | "))
                {
                    return new AlbumSearchModel(true, searchValue.Split(" | ")[0], null, searchValue.Split(" | ")[1], null);
                }

                var result = await this._lastFmService.SearchAlbumAsync(searchValue);
                if (result.Success && result.Content.Any())
                {
                    var album = result.Content[0];

                    return new AlbumSearchModel(true, album.ArtistName, null, album.Name, album.Url.ToString());
                }

                if (result.Success)
                {
                    this._embed.WithDescription($"Album could not be found, please check your search values and try again.");
                    await this.ReplyAsync("", false, this._embed.Build());
                    return new AlbumSearchModel(false);
                }
                this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
                await this.ReplyAsync("", false, this._embed.Build());
                return new AlbumSearchModel(false);
            }

            var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true);

            if (!recentScrobbles.Success || recentScrobbles.Content == null)
            {
                this._embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, this.Context);
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                await ReplyAsync("", false, this._embed.Build());
                return new AlbumSearchModel(false);
            }

            if (!recentScrobbles.Content.RecentTracks.Track.Any())
            {
                this._embed.NoScrobblesFoundErrorResponse(userSettings.UserNameLastFM);
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                await ReplyAsync("", false, this._embed.Build());
                return new AlbumSearchModel(false);
            }

            var currentTrack = recentScrobbles.Content.RecentTracks.Track[0];

            return new AlbumSearchModel(true, currentTrack.Artist.Text, currentTrack.Artist.Url, currentTrack.Album.Text, currentTrack.Url.ToString());
        }

        [Command("topalbums", RunMode = RunMode.Async)]
        [Summary("Displays top albums.")]
        [Alias("abl", "abs", "tab", "albumlist", "top albums", "albums", "albumslist")]
        [UsernameSetRequired]
        public async Task TopAlbumsAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(extraOptions) && extraOptions.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}topalbums options");
                this._embed.WithDescription($"- `{Constants.CompactTimePeriodList}`\n" +
                                            $"- `number of albums (max 16)`\n" +
                                            $"- `user mention/id`");

                this._embed.AddField("Example",
                    $"`{prfx}topalbums alltime 9 @slipper`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timePeriodString = extraOptions;
            if (this.Context.InteractionData != null)
            {
                var time = this.Context.InteractionData.Choices.FirstOrDefault(w => w.Name == "time");
                timePeriodString = time?.Value?.ToLower();
            }

            var amountString = extraOptions;
            if (this.Context.InteractionData != null)
            {
                var time = this.Context.InteractionData.Choices.FirstOrDefault(w => w.Name == "amount");
                amountString = time?.Value?.ToLower();
            }

            var timeSettings = SettingService.GetTimePeriod(timePeriodString);
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(amountString);

            try
            {
                var description = "";
                if (!timeSettings.UsePlays)
                {
                    var albums = await this._lastFmService.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings.LastStatsTimeSpan, amount);
                    if (albums == null || !albums.Any() || !albums.Content.Any())
                    {
                        this._embed.NoScrobblesFoundErrorResponse(albums?.Status, prfx, userSettings.UserNameLastFm);
                        this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    for (var i = 0; i < albums.Count(); i++)
                    {
                        var album = albums.Content[i];

                        if (albums.Count() > 10)
                        {
                            description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.PlayCount} plays) \n";
                        }
                        else
                        {
                            description += $"{i + 1}. **{album.ArtistName}** - **[{album.Name}]({album.Url})** ({album.PlayCount} plays) \n";
                        }
                    }

                    this._embedFooter.WithText($"{albums.TotalItems} different albums in this time period");
                }
                else
                {
                    int userId;
                    if (userSettings.DifferentUser && userSettings.DiscordUserId.HasValue)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId.Value);
                        if (otherUser.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(otherUser);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(otherUser);
                        }

                        userId = otherUser.UserId;
                    }
                    else
                    {
                        if (user.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(user);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(user);
                        }

                        userId = user.UserId;
                    }

                    var albums = await this._playService.GetTopAlbums(userId,
                        timeSettings.PlayDays.GetValueOrDefault());

                    var amountAvailable = albums.Count < amount ? albums.Count : amount;
                    for (var i = 0; i < amountAvailable; i++)
                    {
                        var album = albums[i];
                        description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.Playcount} {StringExtensions.GetPlaysString(album.Playcount)}) \n";
                    }

                    this._embedFooter.WithText($"{albums.Count} different albums in this time period");
                }

                string userTitle;
                if (!userSettings.DifferentUser)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var artistsString = amount == 1 ? "album" : "albums";
                this._embedAuthor.WithName($"Top {amount} {timeSettings.Description.ToLower()} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{timeSettings.UrlParameter}");
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithDescription(description);
                this._embed.WithFooter(this._embedFooter);

                if (this.Context.InteractionData != null)
                {
                    await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                }
                else
                {
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show top albums info due to an internal error.");
            }
        }

        [Command("whoknowsalbum", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same album in your server")]
        [Alias("wa", "wka", "wkab", "wab", "wkab", "wk album", "whoknows album")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task WhoKnowsAsync([Remainder] string albumValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}whoknowsalbum");
                this._embed.WithDescription($"Shows what members in your server listened to the album you're currently listening to or searching for.");

                this._embed.AddField("Examples",
                    $"`{prfx}wa` \n" +
                    $"`{prfx}whoknowsalbum` \n" +
                    $"`{prfx}whoknowsalbum The Beatles Abbey Road` \n" +
                    $"`{prfx}whoknowsalbum Metallica & Lou Reed | Lulu`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-50))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 50 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetGuildAsync(this.Context.Guild.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var searchResult = await this.SearchAlbum(albumValues, userSettings, prfx);
            if (!searchResult.AlbumFound)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
                {"username", userSettings.UserNameLastFM }
            };

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            var album = albumCall.Content.Album;

            var albumName = $"{album.Artist} - {album.Name}";

            try
            {
                var guild = await guildTask;

                var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(this.Context, filteredGuildUsers, album.Artist, album.Name);

                if (album.Userplaycount != 0)
                {
                    usersWithAlbum = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, currentUser, albumName, album.Userplaycount);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithAlbum);
                if (usersWithAlbum.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this album.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows album requested by {userTitle}";

                var rnd = new Random();
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-15))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                {
                    var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingAlbum(userSettings.UserId,
                    this.Context.Guild.Id, album.Artist, album.Name);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                this._embed.WithTitle($"Who knows {albumName} in {this.Context.Guild.Name}");

                if (Uri.IsWellFormedUriString(album.Url, UriKind.Absolute))
                {
                    this._embed.WithUrl(album.Url);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                if (album.Image.Any() && album.Image != null)
                {
                    this._embed.WithThumbnailUrl(album.Image.First(f => f.Size == "mega").Text.ToString());
                }
                
                if (this.Context.InteractionData != null)
                {
                    await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                }
                else
                {
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows album. Please let us know as this feature is in beta.");
            }
        }


        [Command("serveralbums", RunMode = RunMode.Async)]
        [Summary("Shows top albums for your server")]
        [Alias("sab", "stab", "servertopalbums", "serveralbum", "server albums")]
        public async Task GuildAlbumsAsync(params string[] extraOptions)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}serveralbums");

                var helpDescription = new StringBuilder();
                helpDescription.AppendLine("Shows the top albums for your server.");
                helpDescription.AppendLine();
                helpDescription.AppendLine("Available time periods: `weekly` and `alltime`");
                helpDescription.AppendLine("Available order options: `plays` and `listeners`");

                this._embed.WithDescription(helpDescription.ToString());

                this._embed.AddField("Examples",
                    $"`{prfx}sab` \n" +
                    $"`{prfx}sab a p` \n" +
                    $"`{prfx}serveralbums` \n" +
                    $"`{prfx}serveralbums alltime` \n" +
                    $"`{prfx}serveralbums listeners weekly`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverAlbumSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Listeners
            };

            serverAlbumSettings = SettingService.SetGuildRankingSettings(serverAlbumSettings, extraOptions);

            try
            {
                IReadOnlyList<ListAlbum> topGuildAlbums;
                var users = filteredGuildUsers.Select(s => s.User).ToList();
                if (serverAlbumSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildAlbums = await this._whoKnowsAlbumService.GetTopAlbumsForGuild(users, serverAlbumSettings.OrderType);
                    this._embed.WithTitle($"Top alltime albums in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildAlbums = await this._whoKnowsPlayService.GetTopWeekAlbumsForGuild(users, serverAlbumSettings.OrderType);
                    this._embed.WithTitle($"Top weekly albums in {this.Context.Guild.Name}");
                }

                var description = "";
                var footer = "";

                if (serverAlbumSettings.OrderType == OrderType.Listeners)
                {
                    footer += "Listeners / Plays - Ordered by listeners\n";
                }
                else
                {
                    footer += "Listeners / Plays - Ordered by plays\n";
                }

                foreach (var album in topGuildAlbums)
                {
                    description += $"`{album.ListenerCount}` / `{album.Playcount}` | **{album.AlbumName}** by **{album.ArtistName}**\n";
                }

                this._embed.WithDescription(description);

                var rnd = new Random();
                var randomHintNumber = rnd.Next(0, 5);
                if (randomHintNumber == 1)
                {
                    footer += $"View specific album listeners with {prfx}whoknowsalbum\n";
                }
                else if (randomHintNumber == 2)
                {
                    footer += $"Available time periods: alltime and weekly\n";
                }
                else if (randomHintNumber == 3)
                {
                    footer += $"Available sorting options: plays and listeners\n";
                }
                if (guild.LastIndexed < DateTime.UtcNow.AddDays(-15) && randomHintNumber == 4)
                {
                    footer += $"Missing members? Update with {prfx}index\n";
                }

                if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                {
                    var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                    footer += $"{filteredAmount} inactive/blocked users filtered";
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while using serveralbums. Please report this issue.");
            }
        }
    }
}
