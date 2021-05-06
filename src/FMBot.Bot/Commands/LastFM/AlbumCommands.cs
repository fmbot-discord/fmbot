using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
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
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Repositories;
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
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly SpotifyService _spotifyService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly UserService _userService;
        private readonly TrackService _trackService;
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
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmRepository lastFmRepository,
                PlayService playService,
                SettingService settingService,
                UserService userService,
                WhoKnowsAlbumService whoKnowsAlbumService,
                WhoKnowsPlayService whoKnowsPlayService,
                WhoKnowsService whoKnowsService,
                InteractivityService interactivity,
                TrackService trackService,
                SpotifyService spotifyService)
        {
            this._censorService = censorService;
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmRepository = lastFmRepository;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowsAlbumService = whoKnowsAlbumService;
            this._whoKnowsPlayService = whoKnowsPlayService;
            this._whoKnowsService = whoKnowsService;
            this.Interactivity = interactivity;
            this._trackService = trackService;
            this._spotifyService = spotifyService;

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
            try
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

                var album = await this.SearchAlbum(albumValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName(
                    StringExtensions.TruncateLongString($"Info about {album.ArtistName} - {album.AlbumName} for {userTitle}", 255));

                if (album.AlbumUrl != null)
                {
                    this._embed.WithUrl(album.AlbumUrl);
                }

                this._embed.WithAuthor(this._embedAuthor);

                var globalStats = "";
                globalStats += $"`{album.TotalListeners}` {StringExtensions.GetListenersString(album.TotalListeners)}";
                globalStats += $"\n`{album.TotalPlaycount}` global {StringExtensions.GetPlaysString(album.TotalPlaycount)}";
                if (album.UserPlaycount.HasValue)
                {
                    globalStats += $"\n`{album.UserPlaycount}` {StringExtensions.GetPlaysString(album.UserPlaycount)} by you";
                    globalStats += $"\n`{await this._playService.GetWeekAlbumPlaycountAsync(userSettings.UserId, album.AlbumName, album.ArtistName)}` by you last week";
                }

                this._embed.AddField("Last.fm stats", globalStats, true);

                if (!this._guildService.CheckIfDM(this.Context))
                {
                    var serverStats = "";
                    var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

                    if (guild?.LastIndexed != null)
                    {
                        var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(this.Context, guild.GuildId, album.ArtistName, album.AlbumName);
                        var filteredUsersWithAlbum = WhoKnowsService.FilterGuildUsersAsync(usersWithAlbum, guild);

                        var serverListeners = filteredUsersWithAlbum.Count;
                        var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                        var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                        serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                        serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                        serverStats += $"\n`{(int)avgServerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";

                        if (usersWithAlbum.Count > filteredUsersWithAlbum.Count)
                        {
                            var filteredAmount = usersWithAlbum.Count - filteredUsersWithAlbum.Count;
                            serverStats += $"\n`{filteredAmount}` users filtered";
                        }
                    }
                    else
                    {
                        serverStats += "Run `.fmindex` to get server stats";
                    }

                    this._embed.AddField("Server stats", serverStats, true);
                }

                if (album.AlbumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumCoverUrl);
                    if (safeForChannel)
                    {
                        this._embed.WithThumbnailUrl(album.AlbumCoverUrl);
                    }
                }

                if (databaseAlbum?.Label != null)
                {
                    this._embed.WithFooter($"Label: {databaseAlbum.Label}");
                }

                if (album.Description != null)
                {
                    this._embed.AddField("Summary", album.Description);
                }

                if (album.AlbumTracks != null && album.AlbumTracks.Any())
                {
                    var tracks = new StringBuilder();
                    for (int i = 0; i < album.AlbumTracks.Count; i++)
                    {
                        var track = album.AlbumTracks.OrderBy(o => o.Rank).ToList()[i];

                        if (album.AlbumTracks.Count <= 10)
                        {
                            tracks.Append($"{i + 1}. [{track.TrackName}]({track.TrackUrl})");
                        }
                        else
                        {
                            tracks.Append($"{i + 1}. **{track.TrackName}**");
                        }

                        if (track.Duration.HasValue)
                        {
                            var duration = TimeSpan.FromSeconds(track.Duration.Value);
                            var formattedTrackLength = string.Format("{0}{1}:{2:D2}",
                                duration.Hours == 0 ? "" : $"{duration.Hours}:",
                                duration.Minutes,
                                duration.Seconds);
                            tracks.Append($" - `{formattedTrackLength}`");
                        }
                        tracks.AppendLine();
                    }
                    this._embed.AddField("Tracks", tracks.ToString());
                }

                if (album.Tags != null && album.Tags.Any())
                {
                    var tags = LastFmRepository.TagsToLinkedString(album.Tags);

                    this._embed.AddField("Tags", tags);
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
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

            var album = await this.SearchAlbum(userSettings.NewSearchValue, user.UserNameLastFM, userSettings.SessionKeyLastFm, userSettings.UserNameLastFm);
            if (album == null)
            {
                return;
            }

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{album.UserPlaycount}` {StringExtensions.GetPlaysString(album.UserPlaycount)} " +
                $"for **{album.AlbumName.FilterOutMentions()}** by **{album.ArtistName.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && user.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekAlbumPlaycountAsync(userSettings.UserId, album.AlbumName, album.ArtistName);
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

            var album = await this.SearchAlbum(albumValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (album == null)
            {
                return;
            }

            if (album.AlbumCoverUrl == null)
            {
                this._embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                            $"{album.ArtistName} - {album.AlbumName}\n" +
                                            $"[View on last.fm]({album.AlbumUrl})");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var image = await LastFmRepository.GetAlbumImageAsBitmapAsync(album.AlbumCoverUrl);
            if (image == null)
            {
                this._embed.WithDescription("Sorry, something went wrong while getting album cover for this album: \n" +
                                            $"{album.ArtistName} - {album.AlbumName}\n" +
                                            $"[View on last.fm]({album.AlbumUrl})");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                album.AlbumName, album.ArtistName, album.AlbumUrl, this._embed);
            if (!safeForChannel)
            {
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Censored);
                return;
            }

            this._embed.WithDescription($"**{album.ArtistName} - [{album.AlbumName}]({album.AlbumUrl})**");
            this._embedFooter.WithText(
                $"Album cover requested by {await this._userService.GetUserTitleAsync(this.Context)}");
            this._embed.WithFooter(this._embedFooter);

            var imageMemoryStream = new MemoryStream();
            image.Save(imageMemoryStream, ImageFormat.Png);
            imageMemoryStream.Position = 0;

            await this.Context.Channel.SendFileAsync(
                imageMemoryStream,
                $"cover-{StringExtensions.ReplaceInvalidChars($"{album.ArtistName}_{album.AlbumName}")}.png",
                null,
                false,
                this._embed.Build());

            this.Context.LogCommandUsed();
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
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} {artistsString} for {userTitle}");
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{timeSettings.UrlParameter}");

            amount = paginationEnabled ? 100 : amount;

            try
            {
                var description = "";
                if (!timeSettings.UsePlays)
                {
                    var albums = await this._lastFmRepository.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings.LastStatsTimeSpan, amount);
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
                        var album = albums.Content[i];

                        if (albums.Count() > 10 && !paginationEnabled)
                        {
                            description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.PlayCount}  {StringExtensions.GetPlaysString(album.PlayCount)}) \n";
                        }
                        else
                        {
                            var url = album.Url;
                            var escapedAlbumName = Regex.Replace(album.Name, @"([|\\*])", @"\$1");

                            if (user.RymEnabled == true)
                            {
                                url = new Uri(StringExtensions.GetRymUrl(album.Name, album.ArtistName));
                            }

                            description += $"{i + 1}. **{album.ArtistName}** - **[{escapedAlbumName}]({url})** ({album.PlayCount}  {StringExtensions.GetPlaysString(album.PlayCount)}) \n";
                        }

                        var pageAmount = i + 1;
                        if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == amount))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
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
                        var album = albums[i];
                        description += $"{i + 1}. **{album.ArtistName}** - **{album.Name}** ({album.Playcount} {StringExtensions.GetPlaysString(album.Playcount)}) \n";

                        var pageAmount = i + 1;
                        if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == amount))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
                        }
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

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds), runOnGateway: false);
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

            var album = await this.SearchAlbum(albumValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (album == null)
            {
                return;
            }

            var albumName = $"{album.AlbumName} by {album.ArtistName}";

            try
            {
                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(this.Context, guild.GuildId, album.ArtistName, album.AlbumName);

                if (album.UserPlaycount.HasValue)
                {
                    usersWithAlbum = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, currentUser, albumName, album.UserPlaycount);
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
                    this.Context.Guild.Id, album.ArtistName, album.AlbumName);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                this._embed.WithTitle(StringExtensions.TruncateLongString($"{albumName} in {this.Context.Guild.Name}", 255));

                var url = userSettings.RymEnabled == true
                    ? StringExtensions.GetRymUrl(album.AlbumName, album.ArtistName)
                    : album.AlbumUrl;

                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    this._embed.WithUrl(url);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                if (album.AlbumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumCoverUrl);
                    if (safeForChannel)
                    {
                        this._embed.WithThumbnailUrl(album.AlbumCoverUrl);
                    }
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


            var album = await this.SearchAlbum(albumValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (album == null)
            {
                return;
            }

            var albumName = $"{album.AlbumName} by {album.ArtistName}";

            try
            {
                var usersWithArtist = await this._whoKnowsAlbumService.GetGlobalUsersForAlbum(this.Context, album.ArtistName, album.AlbumName);

                if (album.UserPlaycount.HasValue && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, albumName, album.UserPlaycount);
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
                    this.Context.Guild.Id, album.ArtistName, album.AlbumName);

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

                var url = userSettings.RymEnabled == true
                    ? StringExtensions.GetRymUrl(album.AlbumName, album.ArtistName)
                    : album.AlbumUrl;

                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    this._embed.WithUrl(url);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                if (album.AlbumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumCoverUrl);
                    if (safeForChannel)
                    {
                        this._embed.WithThumbnailUrl(album.AlbumCoverUrl);
                    }
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

        [Command("albumtracks", RunMode = RunMode.Async)]
        [Summary("Displays track playcounts for a specific album.")]
        [Alias("abt", "abtracks", "albumt")]
        [UsernameSetRequired]
        public async Task AlbumTracksAsync([Remainder] string albumValues = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var userSettings = await this._settingService.GetUser(albumValues, user, this.Context);

                var album = await this.SearchAlbum(userSettings.NewSearchValue, user.UserNameLastFM, user.SessionKeyLastFm, userSettings.UserNameLastFm);
                if (album == null)
                {
                    return;
                }

                var albumName = $"{album.AlbumName} by {album.ArtistName}";

                var spotifySource = false;

                List<AlbumTrack> albumTracks;
                if (album.AlbumTracks != null && album.AlbumTracks.Any())
                {
                    albumTracks = album.AlbumTracks;
                }
                else
                {
                    var dbAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);
                    if (dbAlbum?.Tracks != null && dbAlbum.Tracks.Any())
                    {
                        albumTracks = dbAlbum.Tracks.Select(s => new AlbumTrack
                        {
                            TrackName = s.Name,
                            ArtistName = album.ArtistName,
                        }).ToList();
                        spotifySource = true;
                    }
                    else
                    {
                        this._embed.WithDescription(
                            $"Sorry, but neither Last.fm or Spotify know the tracks for {albumName}.");

                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return;
                    }
                }

                var albumTracksPlaycounts = await this._trackService.GetAlbumTracksPlaycounts(albumTracks, userSettings.UserId, album.ArtistName);

                if (albumTracksPlaycounts.Count == 0)
                {
                    this._embed.WithDescription(
                        $"{userSettings.DiscordUserName} has no scrobbles for this album, their scrobbles have no album associated with them or neither Spotify and Last.fm know what tracks are in this album.");
                }
                else
                {
                    var description = new StringBuilder();
                    var amountOfDiscs = albumTracks.Count(c => c.Rank == 1) == 0 ? 1 : albumTracks.Count(c => c.Rank == 1);
                    bool maxTracksReached = false;

                    var i = 0;
                    var tracksDisplayed = 0;
                    for (var disc = 1; disc < amountOfDiscs + 1; disc++)
                    {
                        if (tracksDisplayed >= 30)
                        {
                            maxTracksReached = true;
                            break;
                        }

                        if (amountOfDiscs > 1)
                        {
                            description.AppendLine($"`Disc {disc}`");
                        }

                        for (; i < albumTracks.Count; i++)
                        {
                            if (tracksDisplayed >= 30)
                            {
                                maxTracksReached = true;
                                break;
                            }

                            var albumTrack = albumTracks[i];
                            var albumTrackWithPlaycount =
                                albumTracksPlaycounts.FirstOrDefault(f =>
                                    f.Name.ToLower() == albumTrack.TrackName.ToLower());

                            if (albumTrackWithPlaycount != null)
                            {
                                description.AppendLine(
                                    $"{i + 1}. **{albumTrackWithPlaycount.Name}** ({albumTrackWithPlaycount.Playcount} plays)");
                                tracksDisplayed++;
                            }
                        }
                    }

                    this._embed.WithDescription(StringExtensions.TruncateLongString(description.ToString(), 2044));

                    var footer = spotifySource ? "Album source: Spotify | " : "Album source: Last.fm | ";

                    footer += $"{userSettings.DiscordUserName} has {album.UserPlaycount} total scrobbles on this album";

                    if (maxTracksReached)
                    {
                        footer += "\nMax 30 tracks displayed, click on title to view all your tracks on this album.";
                    }

                    this._embed.WithFooter(footer);
                }

                this._embed.WithTitle($"Track playcounts for {albumName}");

                var url = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/music/" +
                          $"{UrlEncoder.Default.Encode(album.ArtistName)}/" +
                          $"{UrlEncoder.Default.Encode(album.AlbumName)}/";
                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    this._embed.WithUrl(url);
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();

            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while using albumtracks. Please report this issue.");
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
                else if (serverAlbumSettings.ChartTimePeriod == ChartTimePeriod.Weekly)
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

        private async Task<AlbumInfo> SearchAlbum(string albumValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null)
        {
            string searchValue;
            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.Length != 0)
            {
                searchValue = albumValues;

                if (searchValue.Contains(" | "))
                {
                    var albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(searchValue.Split(" | ")[0], searchValue.Split(" | ")[1],
                        lastFmUserName);
                    if (!albumInfo.Success && albumInfo.Error == ResponseStatus.MissingParameters)
                    {
                        this._embed.WithDescription($"Album `{searchValue.Split(" | ")[1]}` by `{searchValue.Split(" | ")[0]}`could not be found, please check your search values and try again.");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return null;
                    }
                    if (!albumInfo.Success || albumInfo.Content == null)
                    {
                        this._embed.ErrorResponse(albumInfo.Error, albumInfo.Message, this.Context, "album");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.LastFmError);
                        return null;
                    }
                    return albumInfo.Content;
                }
            }
            else
            {
                var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, useCache: true, sessionKey: sessionKey);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
                {
                    return null;
                }

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];
                var albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName,
                    lastFmUserName);

                if (albumInfo.Content == null || !albumInfo.Success)
                {
                    this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.AlbumName}** by **{lastPlayedTrack.ArtistName}**.\n" +
                                                $"This usually happens on recently released albums or on albums by smaller artists. Please try again later.");

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                return albumInfo.Content;
            }

            var result = await this._lastFmRepository.SearchAlbumAsync(searchValue);
            if (result.Success && result.Content.Any())
            {
                var album = result.Content[0];

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(album.ArtistName, album.Name,
                    lastFmUserName);
                return albumInfo.Content;
            }

            if (result.Success)
            {
                this._embed.WithDescription($"Album could not be found, please check your search values and try again.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.LastFmError);
            return null;
        }
    }
}
