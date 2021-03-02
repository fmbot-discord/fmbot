using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Interactivity;
using Interactivity.Pagination;
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
        private readonly WhoKnowsService _whoKnowsService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;
        private InteractivityService Interactivity { get; }

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
                WhoKnowsPlayService whoKnowsPlayService,
                WhoKnowsService whoKnowsService,
                InteractivityService interactivity)
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
            this._whoKnowsService = whoKnowsService;
            this.Interactivity = interactivity;

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

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);

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
            this._embedAuthor.WithName(StringExtensions.TruncateLongString($"Info about {albumInfo.Artist} - {albumInfo.Name} for {userTitle}", 255));
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
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.ToLower() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}albumplays 'artist and album name'`\n" +
                    "If you don't enter any album name, it will get the plays from the album you're currently listening to.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var userSettings = await this._settingService.GetUser(albumValues, user, this.Context);

            var searchResult = await this.SearchAlbum(userSettings.NewSearchValue, user, prfx);
            if (!searchResult.AlbumFound)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", searchResult.Artist },
                {"album", searchResult.Name },
                {"username", userSettings.UserNameLastFm }
            };

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            var albumInfo = albumCall.Content.Album;

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{albumInfo.Userplaycount}` {StringExtensions.GetPlaysString(albumInfo.Userplaycount)} " +
                $"for **{albumInfo.Name.FilterOutMentions()}** by **{albumInfo.Artist.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && user.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekAlbumPlaycountAsync(userSettings.UserId, albumInfo.Name, albumInfo.Artist);
                if (playsLastWeek != 0)
                {
                    reply += $" (`{playsLastWeek}` last week)";
                }
            }

            await this.Context.Channel.SendMessageAsync(reply);
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

            _ = this.Context.Channel.TriggerTypingAsync();

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

            var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                searchResult.Name, searchResult.Artist, searchResult.Url, this._embed);
            if (!safeForChannel)
            {
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

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                    return new AlbumSearchModel(false);
                }

                this._embed.WithDescription($"Last.fm returned an error: {result.Status}");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                return new AlbumSearchModel(false);
            }

            string sessionKey = null;
            if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
            {
                sessionKey = userSettings.SessionKeyLastFm;
            }

            var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true, sessionKey: sessionKey);

            if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
            {
                return new AlbumSearchModel(false);
            }

            var currentTrack = recentScrobbles.Content.RecentTracks[0];

            return new AlbumSearchModel(true, currentTrack.ArtistName, currentTrack.ArtistName, currentTrack.AlbumName, currentTrack.TrackUrl);
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

            var amountString = extraOptions;

            var timeSettings = SettingService.GetTimePeriod(timePeriodString);
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(amountString);

            var paginationEnabled = false;
            var pages = new List<PageBuilder>();
            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages)
            {
                paginationEnabled = true;
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

            amount = paginationEnabled ? 100 : amount;

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

                    if (albums.Count() <= 10)
                    {
                        paginationEnabled = false;
                    }
                    var footer = $"{albums.TotalItems} different albums in this time period";

                    var rnd = new Random();
                    if (rnd.Next(0, 2) == 1 && albums.Count() > 10 && !paginationEnabled)
                    {
                        footer += $"\nWant pagination? Enable the 'Manage Messages' permission for .fmbot.";
                    }

                    for (var i = 0; i < albums.Count(); i++)
                    {
                        if (paginationEnabled && (i > 0 && i % 10 == 0 || i == amount - 1))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
                        }

                        var album = albums.Content[i];

                        if (albums.Count() > 10 && !paginationEnabled)
                        {
                            description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.PlayCount}  {StringExtensions.GetPlaysString(album.PlayCount)}) \n";
                        }
                        else
                        {
                            description += $"{i + 1}. **{album.ArtistName}** - **[{album.Name}]({album.Url})** ({album.PlayCount}  {StringExtensions.GetPlaysString(album.PlayCount)}) \n";
                        }
                    }

                    this._embedFooter.WithText(footer);
                }
                else
                {
                    int userId;
                    if (userSettings.DifferentUser)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId);
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

                    if (albums.Count <= 10)
                    {
                        paginationEnabled = false;
                    }

                    var footer = $"{albums.Count} different albums in this time period";

                    var amountAvailable = albums.Count < amount ? albums.Count : amount;
                    for (var i = 0; i < amountAvailable; i++)
                    {
                        if (paginationEnabled && (i > 0 && i % 10 == 0 || i == amount - 1))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
                        }

                        var album = albums[i];
                        description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.Playcount} {StringExtensions.GetPlaysString(album.Playcount)}) \n";
                    }

                    this._embedFooter.WithText(footer);
                }

                if (paginationEnabled)
                {
                    var paginator = new StaticPaginatorBuilder()
                        .WithPages(pages)
                        .WithFooter(PaginatorFooter.PageNumber)
                        .WithEmotes(DiscordConstants.PaginationEmotes)
                        .WithTimoutedEmbed(null)
                        .WithCancelledEmbed(null)
                        .WithDeletion(DeletionOptions.Valid)
                        .Build();

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds));
                }
                else
                {
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithDescription(description);
                    this._embed.WithFooter(this._embedFooter);

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
        public async Task WhoKnowsAlbumAsync([Remainder] string albumValues = null)
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
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            var album = albumCall.Content.Album;

            var albumName = $"{album.Name} by {album.Artist}";

            try
            {
                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(this.Context, guild.GuildId, album.Artist, album.Name);

                if (album.Userplaycount != 0)
                {
                    usersWithAlbum = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, currentUser, albumName, album.Userplaycount);
                }

                var filteredUsersWithAlbum = WhoKnowsService.FilterGuildUsersAsync(usersWithAlbum, guild);

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, userSettings.UserId, PrivacyLevel.Server);
                if (filteredUsersWithAlbum.Count == 0)
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

                if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
                {
                    var serverListeners = filteredUsersWithAlbum.Count;
                    var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (usersWithAlbum.Count > filteredUsersWithAlbum.Count)
                {
                    var filteredAmount = usersWithAlbum.Count - filteredUsersWithAlbum.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingAlbum(userSettings.UserId,
                    this.Context.Guild.Id, album.Artist, album.Name);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                this._embed.WithTitle(StringExtensions.TruncateLongString($"{albumName} in {this.Context.Guild.Name}", 255));

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

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows album. Please let us know as this feature is in beta.");
            }
        }

        [Command("globalwhoknowsalbum", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same album on .fmbot")]
        [Alias("gwa", "gwka", "gwab", "gwkab", "globalwhoknows album")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task GlobalWhoKnowsAlbumAsync([Remainder] string albumValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                HidePrivateUsers = false,
                NewSearchValue = albumValues
            };
            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues);

            var searchResult = await this.SearchAlbum(settings.NewSearchValue, userSettings, prfx);
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

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);

            if (!albumCall.Success)
            {
                this._embed.ErrorResponse(albumCall.Error, albumCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            var album = albumCall.Content.Album;

            var albumName = $"{album.Name} by {album.Artist}";

            try
            {
                var usersWithArtist = await this._whoKnowsAlbumService.GetGlobalUsersForAlbum(this.Context, album.Artist, album.Name);

                if (albumCall.Content.Album.Userplaycount != 0 && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, albumName, albumCall.Content.Album.Userplaycount);
                }

                var guild = await guildTask;

                var filteredUsersWithAlbum = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

                filteredUsersWithAlbum =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guild.GuildUsers.ToList());

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, userSettings.UserId, PrivacyLevel.Global, hidePrivateUsers: settings.HidePrivateUsers);
                if (filteredUsersWithAlbum.Count == 0)
                {
                    serverUsers = "Nobody that uses .fmbot has listened to this album.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"Global WhoKnows album requested by {userTitle}";

                if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
                {
                    var serverListeners = filteredUsersWithAlbum.Count;
                    var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingAlbum(userSettings.UserId,
                    this.Context.Guild.Id, album.Artist, album.Name);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                if (userSettings.PrivacyLevel != PrivacyLevel.Global)
                {
                    footer += $"\nYou are currently not globally visible - use '{prfx}privacy global' to enable.";
                }
                if (settings.HidePrivateUsers)
                {
                    footer += "\nAll private users are hidden from results";
                }

                this._embed.WithTitle($"{albumName} globally");

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

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                     "Make sure it has permission to 'Embed links' and 'Attach Images'");
                }
                else
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Something went wrong while using global whoknows album.");
                }
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
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}serveralbums");

                var helpDescription = new StringBuilder();
                helpDescription.AppendLine("Shows the top albums for your server.");
                helpDescription.AppendLine();
                helpDescription.AppendLine("Available time periods: `weekly`, `monthly` and `alltime`");
                helpDescription.AppendLine("Available order options: `plays` and `listeners`");

                this._embed.WithDescription(helpDescription.ToString());

                this._embed.AddField("Examples",
                    $"`{prfx}sab` \n" +
                    $"`{prfx}sab a p` \n" +
                    $"`{prfx}serveralbums` \n" +
                    $"`{prfx}serveralbums alltime` \n" +
                    $"`{prfx}serveralbums listeners weekly`");

                this._embed.WithFooter("Users that are filtered from whoknows also get filtered from these charts.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverAlbumSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Listeners,
                AmountOfDays = 7
            };

            serverAlbumSettings = SettingService.SetGuildRankingSettings(serverAlbumSettings, extraOptions);

            var description = "";
            var footer = "";

            if (guild.GuildUsers != null && guild.GuildUsers.Count > 500 && serverAlbumSettings.ChartTimePeriod == ChartTimePeriod.Monthly)
            {
                serverAlbumSettings.AmountOfDays = 7;
                serverAlbumSettings.ChartTimePeriod = ChartTimePeriod.Weekly;
                footer += "Sorry, monthly time period is not supported on large servers.\n";
            }

            try
            {
                IReadOnlyList<ListAlbum> topGuildAlbums;
                if (serverAlbumSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildAlbums = await WhoKnowsAlbumService.GetTopAllTimeAlbumsForGuild(guild.GuildId, serverAlbumSettings.OrderType);
                    this._embed.WithTitle($"Top alltime albums in {this.Context.Guild.Name}");
                }
                else if(serverAlbumSettings.ChartTimePeriod == ChartTimePeriod.Weekly)
                {
                    topGuildAlbums = await this._whoKnowsPlayService.GetTopAlbumsForGuild(guild.GuildId, serverAlbumSettings.OrderType, serverAlbumSettings.AmountOfDays);
                    this._embed.WithTitle($"Top weekly albums in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildAlbums = await this._whoKnowsPlayService.GetTopAlbumsForGuild(guild.GuildId, serverAlbumSettings.OrderType, serverAlbumSettings.AmountOfDays);
                    this._embed.WithTitle($"Top monthly albums in {this.Context.Guild.Name}");
                }

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
                    description += $"`{album.ListenerCount}` / `{album.TotalPlaycount}` | **{album.AlbumName}** by **{album.ArtistName}**\n";
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
