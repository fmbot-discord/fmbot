using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
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
using Microsoft.Extensions.Options;
using Constants = FMBot.Domain.Constants;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Albums")]
    public class AlbumCommands : BaseCommandModule
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
        private readonly FriendsService _friendsService;
        private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;
        private readonly WhoKnowsService _whoKnowsService;

        private InteractiveService Interactivity { get; }

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
                InteractiveService interactivity,
                TrackService trackService,
                SpotifyService spotifyService,
                IOptions<BotSettings> botSettings,
                FriendsService friendsService) : base(botSettings)
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
            this._friendsService = friendsService;
        }

        [Command("album", RunMode = RunMode.Async)]
        [Summary("Shows album you're currently listening to or searching for.")]
        [Examples(
            "ab",
            "album",
            "album Ventura Anderson .Paak",
            "ab Boy Harsher | Yr Body Is Nothing")]
        [Alias("ab")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Albums)]
        public async Task AlbumAsync([Remainder] string albumValues = null)
        {
            try
            {
                var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

                _ = this.Context.Channel.TriggerTypingAsync();

                var album = await this.SearchAlbum(albumValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);
                databaseAlbum.Tracks = await this._spotifyService.GetExistingAlbumTracks(databaseAlbum.Id);

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
                    globalStats += $"\n`{await this._playService.GetWeekAlbumPlaycountAsync(contextUser.UserId, album.AlbumName, album.ArtistName)}` by you last week";
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

                        if (filteredUsersWithAlbum.Count != 0)
                        {
                            var serverListeners = filteredUsersWithAlbum.Count;
                            var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                            var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                            serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                            serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                            serverStats += $"\n`{(int)avgServerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                        }
                        else
                        {
                            serverStats += $"\nNo listeners in this server.";
                        }

                        if (usersWithAlbum.Count > filteredUsersWithAlbum.Count)
                        {
                            var filteredAmount = usersWithAlbum.Count - filteredUsersWithAlbum.Count;
                            serverStats += $"\n`{filteredAmount}` users filtered";
                        }
                    }
                    else
                    {
                        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
                        serverStats += $"Run `{prfx}index` to get server stats";
                    }

                    this._embed.AddField("Server stats", serverStats, true);
                }

                var albumCoverUrl = album.AlbumCoverUrl;
                if (albumCoverUrl == null && databaseAlbum.SpotifyImageUrl != null)
                {
                    albumCoverUrl = databaseAlbum.SpotifyImageUrl;
                }
                if (albumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumUrl);
                    if (safeForChannel.Result)
                    {
                        this._embed.WithThumbnailUrl(albumCoverUrl);
                    }
                }

                var footer = new StringBuilder();

                if (contextUser.TotalPlaycount.HasValue && album.UserPlaycount is >= 10)
                {
                    footer.AppendLine($"{(decimal)album.UserPlaycount.Value / contextUser.TotalPlaycount.Value:P} of all your scrobbles are on this album");
                }

                if (databaseAlbum?.Label != null)
                {
                    footer.AppendLine($"Label: {databaseAlbum.Label}");
                }

                if (footer.Length > 0)
                {
                    this._embed.WithFooter(footer.ToString());
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
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show album info due to an internal error.");
            }
        }

        [Command("albumplays", RunMode = RunMode.Async)]
        [Summary("Shows playcount for current album or the one you're searching for.\n\n" +
                 "You can also mention another user to see their playcount.")]
        [Examples(
            "abp",
            "albumplays",
            "albumplays @user",
            "albumplays lfm:fm-bot",
            "albumplays The Slow Rush",
            "abp The Beatles | Yesterday",
            "abp The Beatles | Yesterday @user")]
        [Alias("abp", "albumplay", "abplays", "albump", "album plays")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Albums)]
        public async Task AlbumPlaysAsync([Remainder] string albumValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);

            var album = await this.SearchAlbum(userSettings.NewSearchValue, contextUser.UserNameLastFM, userSettings.SessionKeyLastFm, userSettings.UserNameLastFm);
            if (album == null)
            {
                return;
            }

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{album.UserPlaycount}` {StringExtensions.GetPlaysString(album.UserPlaycount)} " +
                $"for **{album.AlbumName.FilterOutMentions()}** by **{album.ArtistName.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && contextUser.LastUpdated != null)
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
        [Summary("Cover for current album or the one you're searching for.")]
        [Examples(
            "co",
            "cover",
            "cover la priest inji")]
        [Alias("abc", "abco", "co", "albumcover", "album cover")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Albums, CommandCategory.Charts)]
        public async Task AlbumCoverAsync([Remainder] string albumValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var album = await this.SearchAlbum(albumValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);

                var albumCoverUrl = album.AlbumCoverUrl;
                if (albumCoverUrl == null && databaseAlbum.SpotifyImageUrl != null)
                {
                    albumCoverUrl = databaseAlbum.SpotifyImageUrl;
                }

                if (albumCoverUrl == null)
                {
                    this._embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                                $"{album.ArtistName} - {album.AlbumName}\n" +
                                                $"[View on last.fm]({album.AlbumUrl})");
                    await this.ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var image = await this._lastFmRepository.GetAlbumImageAsStreamAsync(albumCoverUrl);
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
                if (!safeForChannel.Result)
                {
                    await this.ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.Censored);
                    return;
                }

                this._embed.WithDescription($"**{album.ArtistName} - [{album.AlbumName}]({album.AlbumUrl})**");
                this._embedFooter.WithText(
                    $"Album cover requested by {await this._userService.GetUserTitleAsync(this.Context)}");
                this._embed.WithFooter(this._embedFooter);


                await this.Context.Channel.SendFileAsync(
                    image,
                    $"cover-{StringExtensions.ReplaceInvalidChars($"{album.ArtistName}_{album.AlbumName}")}.png",
                    null,
                    false,
                    this._embed.Build());

                await image.DisposeAsync();

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show album cover due to an internal error.");
            }
        }

        [Command("topalbums", RunMode = RunMode.Async)]
        [Summary("Shows your or someone else their top albums over a certain time period.")]
        [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
        [Examples("tab", "topalbums", "tab a lfm:fm-bot", "topalbums weekly @user")]
        [Alias("abl", "abs", "tab", "albumlist", "top albums", "albums", "albumslist")]
        [UsernameSetRequired]
        [SupportsPagination]
        [CommandCategories(CommandCategory.Albums)]
        public async Task TopAlbumsAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(extraOptions);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var pages = new List<PageBuilder>();

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

            if (!userSettings.DifferentUser)
            {
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            }
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} albums for {userTitle}");
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{timeSettings.UrlParameter}");

            const int amount = 200;

            try
            {

                var albums = await this._lastFmRepository.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings, amount);
                if (!albums.Success || albums.Content == null)
                {
                    this._embed.ErrorResponse(albums.Error, albums.Message, this.Context);
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                var albumPages = albums.Content.TopAlbums.ChunkBy(10);

                var counter = 1;
                var pageCounter = 1;
                foreach (var albumPage in albumPages)
                {
                    var albumPageString = new StringBuilder();
                    foreach (var album in albumPage)
                    {
                        var url = album.AlbumUrl;
                        var escapedAlbumName = Regex.Replace(album.AlbumName, @"([|\\*])", @"\$1");

                        if (contextUser.RymEnabled == true)
                        {
                            url = StringExtensions.GetRymUrl(album.AlbumName, album.ArtistName);
                        }

                        albumPageString.AppendLine($"{counter}. **{album.ArtistName}** - **[{escapedAlbumName}]({url})** ({album.UserPlaycount} {StringExtensions.GetPlaysString(album.UserPlaycount)})");
                        counter++;
                    }

                    var footer = $"Page {pageCounter}/{albumPages.Count}";
                    if (albums.Content.TotalAmount.HasValue && albums.Content.TotalAmount.Value != amount)
                    {
                        footer += $" - {albums.Content.TotalAmount} different albums in this time period";
                    }

                    pages.Add(new PageBuilder()
                        .WithDescription(albumPageString.ToString())
                        .WithAuthor(this._embedAuthor)
                        .WithFooter(footer));
                    pageCounter++;
                }

                if (!pages.Any())
                {
                    pages.Add(new PageBuilder()
                        .WithDescription("No albums played in this time period.")
                        .WithAuthor(this._embedAuthor));
                }

                var paginator = StringService.BuildStaticPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator,
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show top albums info due to an internal error.");
            }
        }

        [Command("whoknowsalbum", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to an album in your server")]
        [Alias("wa", "wka", "wkab", "wab", "wkab", "wk album", "whoknows album")]
        [Examples("wa", "whoknowsalbum", "whoknowsalbum the beatles abbey road", "whoknowsalbum Metallica & Lou Reed | Lulu")]
        [UsernameSetRequired]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
        public async Task WhoKnowsAlbumAsync([Remainder] string albumValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            try
            {
                var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

                _ = this.Context.Channel.TriggerTypingAsync();

                var album = await this.SearchAlbum(albumValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);

                var albumName = $"{album.AlbumName} by {album.ArtistName}";


                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateGuildUser(await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), currentUser.UserId, guild);

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
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-30))
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

                if (usersWithAlbum.Count > filteredUsersWithAlbum.Count && !guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    var filteredAmount = usersWithAlbum.Count - filteredUsersWithAlbum.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                if (guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    footer += $"\nUsers with WhoKnows whitelisted role only";
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

                var albumCoverUrl = album.AlbumCoverUrl;
                if (albumCoverUrl == null && databaseAlbum.SpotifyImageUrl != null)
                {
                    albumCoverUrl = databaseAlbum.SpotifyImageUrl;
                }
                if (albumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumUrl);
                    if (safeForChannel.Result)
                    {
                        this._embed.WithThumbnailUrl(albumCoverUrl);
                    }
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows album. Please report this issue.");
            }
        }

        [Command("globalwhoknowsalbum", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the an album on .fmbot")]
        [Examples("gwa", "globalwhoknowsalbum", "globalwhoknowsalbum the beatles abbey road", "globalwhoknowsalbum Metallica & Lou Reed | Lulu")]
        [Alias("gwa", "gwka", "gwab", "gwkab", "globalwka", "globalwkalbum", "globalwhoknows album")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
        public async Task GlobalWhoKnowsAlbumAsync([Remainder] string albumValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            try
            {
                var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild?.Id);
                _ = this.Context.Channel.TriggerTypingAsync();

                var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

                var currentSettings = new WhoKnowsSettings
                {
                    HidePrivateUsers = false,
                    ShowBotters = false,
                    AdminView = false,
                    NewSearchValue = albumValues
                };

                var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, userSettings.UserType);

                var album = await this.SearchAlbum(settings.NewSearchValue, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);

                var albumName = $"{album.AlbumName} by {album.ArtistName}";

                var usersWithAlbum = await this._whoKnowsAlbumService.GetGlobalUsersForAlbum(this.Context, album.ArtistName, album.AlbumName);

                if (album.UserPlaycount.HasValue && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithAlbum = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, guildUser, albumName, album.UserPlaycount);
                }

                var filteredUsersWithAlbum = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithAlbum);

                var guild = await guildTask;
                var privacyLevel = PrivacyLevel.Global;

                if (guild != null)
                {
                    filteredUsersWithAlbum =
                        WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guild.GuildUsers.ToList());

                    if (settings.AdminView && guild.SpecialGuild == true)
                    {
                        privacyLevel = PrivacyLevel.Server;
                    }
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, userSettings.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
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
                    this.Context.Guild?.Id, album.ArtistName, album.AlbumName);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                if (settings.AdminView)
                {
                    footer += "\nAdmin view enabled - not for public channels";
                }
                if (userSettings.PrivacyLevel != PrivacyLevel.Global)
                {
                    footer += $"\nYou are currently not globally visible - use " +
                        $"'{prfx}privacy global' to enable.";
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

                var albumCoverUrl = album.AlbumCoverUrl;
                if (albumCoverUrl == null && databaseAlbum.SpotifyImageUrl != null)
                {
                    albumCoverUrl = databaseAlbum.SpotifyImageUrl;
                }
                if (albumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumUrl);
                    if (safeForChannel.Result)
                    {
                        this._embed.WithThumbnailUrl(albumCoverUrl);
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

        [Command("friendwhoknowsalbum", RunMode = RunMode.Async)]
        [Summary("Shows who of your friends listen to an album in .fmbot")]
        [Examples("fwa", "fwka COMA", "friendwhoknows", "friendwhoknowsalbum the beatles abbey road", "friendwhoknowsalbum Metallica & Lou Reed | Lulu")]
        [Alias("fwa", "fwka", "fwkab", "fwab", "friendwhoknows album", "friends whoknows album", "friend whoknows album")]
        [UsernameSetRequired]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows, CommandCategory.Friends)]
        public async Task FriendWhoKnowsAlbumAsync([Remainder] string albumValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var user = await this._userService.GetUserWithFriendsAsync(this.Context.User);

                if (user.Friends?.Any() != true)
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                     $"`{prfx}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

                var album = await this.SearchAlbum(albumValues, user.UserNameLastFM, user.SessionKeyLastFm);
                if (album == null)
                {
                    return;
                }

                var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(album);

                var albumName = $"{album.AlbumName} by {album.ArtistName}";

                var usersWithAlbum = await this._whoKnowsAlbumService.GetFriendUsersForAlbum(this.Context, guild.GuildId, user.UserId, album.ArtistName, album.AlbumName);

                if (album.UserPlaycount.HasValue && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(user.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : user.UserNameLastFM,
                        User = user
                    };
                    usersWithAlbum = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, guildUser, albumName, album.UserPlaycount);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithAlbum, user.UserId, PrivacyLevel.Server);
                if (usersWithAlbum.Count == 0)
                {
                    serverUsers = "None of your friends have listened to this album.";
                }

                this._embed.WithDescription(serverUsers);

                var footer = "";

                var amountOfHiddenFriends = user.Friends.Count(c => !c.FriendUserId.HasValue);
                if (amountOfHiddenFriends > 0)
                {
                    footer += $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                footer += $"\nFriends WhoKnow album requested by {userTitle}";

                if (usersWithAlbum.Any() && usersWithAlbum.Count > 1)
                {
                    var globalListeners = usersWithAlbum.Count;
                    var globalPlaycount = usersWithAlbum.Sum(a => a.Playcount);
                    var avgPlaycount = usersWithAlbum.Average(a => a.Playcount);

                    footer += $"\n{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ";
                    footer += $"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ";
                    footer += $"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}";
                }

                this._embed.WithTitle($"{albumName} with friends");

                if (Uri.IsWellFormedUriString(album.AlbumUrl, UriKind.Absolute))
                {
                    this._embed.WithUrl(album.AlbumUrl);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                var albumCoverUrl = album.AlbumCoverUrl;
                if (albumCoverUrl == null && databaseAlbum.SpotifyImageUrl != null)
                {
                    albumCoverUrl = databaseAlbum.SpotifyImageUrl;
                }
                if (albumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(this.Context,
                        album.AlbumName, album.ArtistName, album.AlbumUrl);
                    if (safeForChannel.Result)
                    {
                        this._embed.WithThumbnailUrl(albumCoverUrl);
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
                    await ReplyAsync("Something went wrong while using friend whoknows album.");
                }
            }
        }

        [Command("albumtracks", RunMode = RunMode.Async)]
        [Summary("Shows track playcounts for a specific album")]
        [Examples("abt", "albumtracks", "albumtracks de jeugd van tegenwoordig machine", "albumtracks U2 | The Joshua Tree")]
        [Alias("abt", "abtracks", "albumt")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Albums)]
        public async Task AlbumTracksAsync([Remainder] string albumValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);

                var album = await this.SearchAlbum(userSettings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
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
                    dbAlbum.Tracks = await this._spotifyService.GetExistingAlbumTracks(dbAlbum.Id);

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
                await ReplyAsync("Something went wrong while using albumtracks. Please report this issue.");
            }
        }

        [Command("serveralbums", RunMode = RunMode.Async)]
        [Summary("Top albums for your server, optionally for a specific artist.")]
        [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`", "Artist name")]
        [Examples("sab", "sab a p", "serveralbums", "serveralbums alltime", "serveralbums listeners weekly", "serveralbums the beatles monthly")]
        [Alias("sab", "stab", "servertopalbums", "serveralbum", "server albums")]
        [RequiresIndex]
        [GuildOnly]
        [CommandCategories(CommandCategory.Albums)]
        public async Task GuildAlbumsAsync([Remainder] string guildAlbumsOptions = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverAlbumSettings = new GuildRankingSettings
            {
                ChartTimePeriod = TimePeriod.Weekly,
                TimeDescription = "weekly",
                OrderType = OrderType.Listeners,
                AmountOfDays = 7,
                NewSearchValue = guildAlbumsOptions
            };

            try
            {
                serverAlbumSettings = SettingService.SetGuildRankingSettings(serverAlbumSettings, guildAlbumsOptions);
                var foundTimePeriod = SettingService.GetTimePeriod(serverAlbumSettings.NewSearchValue, serverAlbumSettings.ChartTimePeriod);
                var artistName = foundTimePeriod.NewSearchValue;

                if (foundTimePeriod.UsePlays || foundTimePeriod.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
                {
                    serverAlbumSettings.ChartTimePeriod = foundTimePeriod.TimePeriod;
                    serverAlbumSettings.TimeDescription = foundTimePeriod.Description;
                    serverAlbumSettings.AmountOfDays = foundTimePeriod.PlayDays.GetValueOrDefault();
                }

                var description = "";
                var footer = "";

                if (guild.GuildUsers != null && guild.GuildUsers.Count > 500 && serverAlbumSettings.ChartTimePeriod == TimePeriod.Monthly)
                {
                    serverAlbumSettings.AmountOfDays = 7;
                    serverAlbumSettings.ChartTimePeriod = TimePeriod.Weekly;
                    serverAlbumSettings.TimeDescription = "weekly";
                    footer += "Sorry, monthly time period is not supported on large servers.\n";
                }

                IReadOnlyList<ListAlbum> topGuildAlbums;
                if (serverAlbumSettings.ChartTimePeriod == TimePeriod.AllTime)
                {
                    topGuildAlbums = await this._whoKnowsAlbumService.GetTopAllTimeAlbumsForGuild(guild.GuildId, serverAlbumSettings.OrderType, artistName);
                }
                else
                {
                    topGuildAlbums = await this._whoKnowsPlayService.GetTopAlbumsForGuild(guild.GuildId, serverAlbumSettings.OrderType, serverAlbumSettings.AmountOfDays, artistName);
                }

                if (string.IsNullOrWhiteSpace(artistName))
                {
                    this._embed.WithTitle($"Top {serverAlbumSettings.TimeDescription.ToLower()} albums in {this.Context.Guild.Name}");
                }
                else
                {
                    this._embed.WithTitle($"Top {serverAlbumSettings.TimeDescription.ToLower()} '{artistName}' albums in {this.Context.Guild.Name}");
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
                    footer += $"Available time periods: alltime, weekly and daily\n";
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
                    if (otherUserUsername != null)
                    {
                        lastFmUserName = otherUserUsername;
                    }

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

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
                {
                    return null;
                }

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

                if (string.IsNullOrWhiteSpace(lastPlayedTrack.AlbumName))
                {
                    this._embed.WithDescription($"The track you're scrobbling (**{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**) does not have an album associated with it according to Last.fm.\n" +
                                                $"Please not that .fmbot is not associated with Last.fm.");

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                var albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName,
                    lastFmUserName);

                if (albumInfo?.Content == null || !albumInfo.Success)
                {
                    this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.AlbumName}** by **{lastPlayedTrack.ArtistName}**.\n" +
                                                $"This usually happens on recently released albums or on albums by smaller artists. Please try again later.\n\n" +
                                                $"Please not that .fmbot is not associated with Last.fm.");

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
