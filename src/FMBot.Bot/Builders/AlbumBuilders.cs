using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.AppleMusic;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Primitives;
using NetCord;
using NetCord.Rest;
using SkiaSharp;

namespace FMBot.Bot.Builders;

public class AlbumBuilders
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly AlbumService _albumService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly PlayService _playService;
    private readonly SpotifyService _spotifyService;
    private readonly TrackService _trackService;
    private readonly TimeService _timeService;
    private readonly CensorService _censorService;
    private readonly UpdateService _updateService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SupporterService _supporterService;
    private readonly IndexService _indexService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly PuppeteerService _puppeteerService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly FeaturedService _featuredService;
    private readonly MusicDataFactory _musicDataFactory;
    private readonly AppleMusicVideoService _appleMusicVideoService;
    private readonly FriendsService _friendsService;

    public AlbumBuilders(UserService userService,
        GuildService guildService,
        AlbumService albumService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        PlayService playService,
        SpotifyService spotifyService,
        TrackService trackService,
        UpdateService updateService,
        TimeService timeService,
        CensorService censorService,
        IDataSourceFactory dataSourceFactory,
        SupporterService supporterService,
        IndexService indexService,
        WhoKnowsPlayService whoKnowsPlayService,
        PuppeteerService puppeteerService,
        WhoKnowsService whoKnowsService,
        FeaturedService featuredService,
        MusicDataFactory musicDataFactory, AppleMusicVideoService appleMusicVideoService,
        FriendsService friendsService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._albumService = albumService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._playService = playService;
        this._spotifyService = spotifyService;
        this._trackService = trackService;
        this._updateService = updateService;
        this._timeService = timeService;
        this._censorService = censorService;
        this._dataSourceFactory = dataSourceFactory;
        this._supporterService = supporterService;
        this._indexService = indexService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._puppeteerService = puppeteerService;
        this._whoKnowsService = whoKnowsService;
        this._featuredService = featuredService;
        this._musicDataFactory = musicDataFactory;
        this._appleMusicVideoService = appleMusicVideoService;
        this._friendsService = friendsService;
    }

    public async Task<ResponseModel> AlbumAsync(ContextModel context,
        string searchValue, UserSettingsModel userSettings = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            albumSearch.Response.ResponseType = ResponseType.ComponentsV2;
            albumSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return albumSearch.Response;
        }

        var databaseAlbumTask = this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);
        var userTitleTask = this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var artistUserTracksTask = this._trackService.GetArtistUserTracks(context.ContextUser.UserId, albumSearch.Album.ArtistName);
        var featuredHistoryTask = this._featuredService.GetAlbumFeaturedHistory(albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

        var databaseAlbum = await databaseAlbumTask;

        Guild guild = null;
        IDictionary<int, FullGuildUser> guildUsers = null;
        Task<IList<WhoKnowsObjectWithUser>> indexedUsersTask = null;
        Task<List<FeaturedLog>> guildFeaturedHistoryTask = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
            guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            if (guild != null)
            {
                guildFeaturedHistoryTask = this._featuredService.GetGuildAlbumFeaturedHistory(guild,
                    albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
            }

            if (guild?.LastIndexed != null && databaseAlbum != null)
            {
                indexedUsersTask = this._whoKnowsAlbumService.GetIndexedUsersForAlbum(context.DiscordGuild,
                    guildUsers, guild.GuildId, databaseAlbum.Id);
            }
        }
        var userTitle = await userTitleTask;
        var artistUserTracks = await artistUserTracksTask;
        var featuredHistory = await featuredHistoryTask;
        var guildFeaturedHistory = guildFeaturedHistoryTask != null ? await guildFeaturedHistoryTask : null;

        // Album cover + accent color
        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        var showThumbnail = false;
        if (albumCoverUrl != null)
        {
            var safeForChannelTask = this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl);
            var accentColorTask = this._albumService.GetAccentColorWithAlbum(context,
                albumCoverUrl, databaseAlbum?.Id, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            if (await safeForChannelTask == CensorService.CensorResult.Safe)
            {
                showThumbnail = true;
            }

            response.ComponentsContainer.WithAccentColor(await accentColorTask);
        }

        // === Section 1: Header ===
        var albumTypeKey = databaseAlbum?.Type switch
        {
            "single" => "album.typeSingleBy",
            "compilation" => "album.typeCompilationBy",
            _ => "album.typeAlbumBy"
        };

        var headerSection = new StringBuilder();
        headerSection.AppendLine(albumSearch.Album.AlbumUrl != null
            ? $"## {StringExtensions.MarkdownLink(albumSearch.Album.AlbumName, albumSearch.Album.AlbumUrl)}"
            : $"## {albumSearch.Album.AlbumName}");
        headerSection.AppendLine(context.Localize(albumTypeKey,
            ("artist", albumSearch.Album.ArtistUrl != null
                ? StringExtensions.MarkdownLink(albumSearch.Album.ArtistName, albumSearch.Album.ArtistUrl)
                : albumSearch.Album.ArtistName)));

        if (databaseAlbum.ReleaseDate != null)
        {
            headerSection.AppendLine(context.Localize("album.releasedOn",
                ("date", AlbumService.GetAlbumReleaseDate(databaseAlbum))));
        }

        if (databaseAlbum?.Label != null)
        {
            headerSection.Append(context.Localize("album.label", ("label", databaseAlbum.Label)));
        }

        if (showThumbnail)
        {
            response.ComponentsContainer.WithSection([
                new TextDisplayProperties(headerSection.ToString().TrimEnd())
            ], albumCoverUrl);
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(headerSection.ToString().TrimEnd()));
        }

        // === Section 2: User stats ===
        if (albumSearch.Album.UserPlaycount.HasValue)
        {
            var correctPlaycountTask = this._updateService.CorrectUserAlbumPlaycount(context.ContextUser.UserId,
                albumSearch.Album.ArtistName,
                albumSearch.Album.AlbumName, albumSearch.Album.UserPlaycount.Value);
            var recentPlaycountsTask = this._playService.GetRecentAlbumPlaycounts(
                context.ContextUser.UserId, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            Task<DateTime?> firstPlayTask = null;
            Task<DateTime?> lastPlayTask = null;
            if (context.ContextUser.UserType != UserType.User && albumSearch.Album.UserPlaycount > 0)
            {
                firstPlayTask = this._playService.GetAlbumFirstPlayDate(context.ContextUser.UserId,
                    albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
                lastPlayTask = this._playService.GetAlbumLastPlayDate(context.ContextUser.UserId,
                    albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
            }

            await correctPlaycountTask;
            var recentPlaycounts = await recentPlaycountsTask;
            var userStats = new StringBuilder();

            var playsLine = recentPlaycounts.month > 0
                ? context.LocalizeCount("album.playsByLastMonth", albumSearch.Album.UserPlaycount.Value,
                    ("user", userTitle),
                    ("month", recentPlaycounts.month.Format(context.NumberFormat)))
                : context.LocalizeCount("album.playsBy", albumSearch.Album.UserPlaycount.Value,
                    ("user", userTitle));

            userStats.AppendLine(playsLine);

            if (albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Count != 0 && artistUserTracks.Count != 0)
            {
                var listeningTime = await this._timeService.GetAllTimePlayTimeForAlbum(albumSearch.Album.AlbumTracks,
                    artistUserTracks, albumSearch.Album.UserPlaycount.Value);
                if (context.ContextUser.TotalPlaycount is > 0 && albumSearch.Album.UserPlaycount is >= 30)
                {
                    userStats.Append(context.Localize("album.listeningTimePercentage",
                        ("time", context.Localizer.LongListeningTime(listeningTime)),
                        ("percentage", ((decimal)albumSearch.Album.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat))));
                }
                else
                {
                    userStats.Append(context.Localize("album.listeningTime",
                        ("time", context.Localizer.LongListeningTime(listeningTime))));
                }
                userStats.AppendLine();

                if (albumSearch.Album.AlbumTracks.Count > 1 && SupporterService.IsSupporter(userSettings?.UserType))
                {
                    var tracksHeard = albumSearch.Album.AlbumTracks.Count(t =>
                        artistUserTracks.Any(ut => StringExtensions.SanitizeTrackNameForComparison(t.TrackName)
                            .Equals(StringExtensions.SanitizeTrackNameForComparison(ut.Name))));
                    userStats.AppendLine(context.Localize("album.tracksListened",
                        ("heard", tracksHeard.Format(context.NumberFormat)),
                        ("total", albumSearch.Album.AlbumTracks.Count.Format(context.NumberFormat))));
                }
            }

            if (firstPlayTask != null)
            {
                var firstPlay = await firstPlayTask;
                if (firstPlay != null)
                {
                    var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();
                    userStats.AppendLine(context.Localize("album.discovered", ("date", $"<t:{firstListenValue}:D>")));
                }

                if (lastPlayTask != null)
                {
                    var lastPlay = await lastPlayTask;
                    if (lastPlay != null && (firstPlay == null || lastPlay.Value.Date > firstPlay.Value.Date))
                    {
                        var lastListenValue = ((DateTimeOffset)lastPlay).ToUnixTimeSeconds();
                        userStats.AppendLine(context.Localize("album.lastListened", ("date", $"<t:{lastListenValue}:D>")));
                    }
                }
            }
            else
            {
                var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
                if (randomHintNumber == 1 &&
                    this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType,
                        context.DiscordGuild?.Id))
                {
                    this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                    userStats.AppendLine(context.Localize("album.supporterDiscoveryPromo",
                        ("url", Constants.GetSupporterOverviewLink)));
                }
            }

            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(userStats.ToString().TrimEnd()));
        }

        // === Section 3: Server + global stats ===
        var statsSection = new StringBuilder();

        if (context.DiscordGuild != null)
        {
            if (indexedUsersTask != null)
            {
                var usersWithAlbum = await indexedUsersTask;
                var (_, filteredUsersWithAlbum) =
                    WhoKnowsService.FilterWhoKnowsObjects(usersWithAlbum, guildUsers, guild, context.ContextUser.UserId);

                if (filteredUsersWithAlbum.Count != 0)
                {
                    var serverListeners = filteredUsersWithAlbum.Count;
                    var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);

                    statsSection.AppendLine(context.LocalizeCount("album.serverStats", serverPlaycount,
                        ("listeners", context.LocalizeCount("album.listenersBold", serverListeners))));
                }
            }

            var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingAlbum(context.Localizer, context.ContextUser.UserId,
                guildUsers, guild, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

            if (guildAlsoPlaying != null)
            {
                statsSection.AppendLine(guildAlsoPlaying);
            }
        }

        statsSection.AppendLine(context.LocalizeCount("album.globalStats",
            albumSearch.Album.TotalPlaycount,
            ("listeners", context.LocalizeCount("album.listenersBold", albumSearch.Album.TotalListeners))));

        var metaLine = new StringBuilder();
        if (databaseAlbum?.Popularity is > 0)
        {
            metaLine.Append(context.Localize("album.popularity", ("value", databaseAlbum.Popularity.Value.Format(context.NumberFormat))));
        }

        if (featuredHistory.Any() || guildFeaturedHistory is { Count: > 0 })
        {
            if (metaLine.Length > 0) metaLine.Append(" — ");
            metaLine.Append(FeaturedService.GetFeaturedTimesString(featuredHistory.Count, guildFeaturedHistory?.Count ?? 0));
        }

        if (metaLine.Length > 0)
        {
            statsSection.AppendLine(metaLine.ToString());
        }

        if (albumSearch.IsRandom)
        {
            statsSection.AppendLine(context.LocalizeCount("album.randomPosition",
                albumSearch.RandomAlbumPlaycount.GetValueOrDefault(),
                ("position", albumSearch.RandomAlbumPosition.Format(context.NumberFormat))));
        }

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(statsSection.ToString().TrimEnd()));

        // === Section 4: Summary ===
        if (albumSearch.Album.Description != null)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(albumSearch.Album.Description));
        }

        // === Section 5: Discogs collection ===
        if (context.ContextUser.UserDiscogs != null && context.ContextUser.DiscogsReleases.Any())
        {
            var albumCollection = context.ContextUser.DiscogsReleases.Where(w =>
                    (w.Release.Title.StartsWith(albumSearch.Album.AlbumName, StringComparison.OrdinalIgnoreCase) ||
                     albumSearch.Album.AlbumName.StartsWith(w.Release.Title, StringComparison.OrdinalIgnoreCase))
                    &&
                    (w.Release.Artist.StartsWith(albumSearch.Album.ArtistName, StringComparison.OrdinalIgnoreCase) ||
                     albumSearch.Album.ArtistName.StartsWith(w.Release.Artist, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (albumCollection.Any())
            {
                var discogsText = new StringBuilder();
                foreach (var album in albumCollection.Take(4))
                {
                    discogsText.Append(StringService.UserDiscogsReleaseToString(album));
                }

                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(discogsText.ToString().TrimEnd()));
            }
        }

        var viewingUserId = userSettings?.DiscordUserId ?? context.ContextUser.DiscordUserId;
        var actionRow = new ActionRowProperties()
            .WithButton(
                context.Localize("album.buttons.tracks"),
                $"{InteractionConstants.Album.Tracks}:{databaseAlbum.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Standard("🎶"))
            .WithButton(
                context.Localize("album.buttons.cover"),
                $"{InteractionConstants.Album.Cover}:{databaseAlbum.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}:motion:",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Standard("🖼️"));
        response.ComponentsContainer.WithActionRow(actionRow);

        return response;
    }

    public async Task<ResponseModel> WhoKnowsAlbumAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string albumValues,
        bool displayRoleSelector = false,
        List<ulong> roles = null,
        bool filterDisabled = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, albumValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedAlbums: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var databaseAlbum = await this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);
        var fullAlbumName = $"{albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}";

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(context.DiscordGuild, guildUsers,
            guild.GuildId, databaseAlbum.Id);

        var discordGuildUser = await context.DiscordGuild.GetCachedGuildUserAsync(context.ContextUser.DiscordUserId);
        var currentUser =
            await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);

        usersWithAlbum = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, context.ContextUser,
            fullAlbumName, context.DiscordGuild, albumSearch.Album.UserPlaycount);

        var (filterStats, filteredUsersWithAlbum) =
            WhoKnowsService.FilterWhoKnowsObjects(usersWithAlbum, guildUsers, guild, context.ContextUser.UserId, roles,
                filterDisabled);

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        if (albumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnail(albumCoverUrl);
            }
            else
            {
                albumCoverUrl = null;
            }

            var accentColor = await this._albumService.GetAccentColorWithAlbum(context,
                albumCoverUrl, databaseAlbum?.Id, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            response.Embed.WithColor(accentColor);
        }

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows(context.Localize("album.whoknows.imageHeader"),
                context.Localize("album.whoknows.imageInServer", ("server", context.DiscordGuild.Name)), albumCoverUrl, fullAlbumName,
                filteredUsersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"whoknows-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = StringExtensions.TruncateLongString(context.Localize("album.whoknows.title",
            ("album", albumSearch.Album.AlbumName),
            ("artist", albumSearch.Album.ArtistName),
            ("server", context.DiscordGuild.Name)), 255);

        var footer = new StringBuilder();

        if (albumSearch.IsRandom)
        {
            footer.AppendLine(context.LocalizeCount("album.randomPosition",
                albumSearch.RandomAlbumPlaycount.GetValueOrDefault(),
                ("position", albumSearch.RandomAlbumPosition.Format(context.NumberFormat))));
        }

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.DiscordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
        {
            footer.AppendLine(context.Localize("album.whoknows.missingMembers",
                ("command", $"{context.Prefix}refreshmembers")));
        }

        var filterDescription = filterStats.GetFullDescription(context.Localizer);
        if (filterDescription != null)
        {
            footer.AppendLine(filterDescription);
        }

        if (filterDisabled)
        {
            footer.AppendLine(context.Localize("album.whoknows.filtersDisabled"));
        }

        if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
        {
            var serverListeners = filteredUsersWithAlbum.Count;
            var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

            footer.AppendLine(context.Localize("album.whoknows.serverStats",
                ("listeners", context.LocalizeCount("shared.listeners", serverListeners)),
                ("plays", context.LocalizeCount("shared.plays", serverPlaycount)),
                ("avg", ((int)avgServerPlaycount).Format(context.NumberFormat))));
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingAlbum(context.Localizer, context.ContextUser.UserId,
            guildUsers, guild, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (albumSearch.LatestScrobble is { NowPlaying: false, TimePlayed: not null } &&
            albumSearch.LatestScrobble.TimePlayed < DateTime.UtcNow.AddHours(-2))
        {
            footer.AppendLine(context.Localize("album.whoknows.outOfSync",
                ("command", $"{context.Prefix}outofsync")));
        }

        var closeFriendUserIds = await this._friendsService.GetCloseFriendUserIdsAsync(context.ContextUser);

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithAlbum,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString(), closeFriendUserIds: closeFriendUserIds);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, context.ContextUser.UserId,
                PrivacyLevel.Server, context.NumberFormat, closeFriendUserIds: closeFriendUserIds);
        if (filteredUsersWithAlbum.Count == 0)
        {
            serverUsers = context.Localize("album.whoknows.noListeners");
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        var url = context.ContextUser.RymEnabled == true
            ? StringExtensions.GetRymUrl(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName)
            : albumSearch.Album.AlbumUrl;

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            response.Embed.WithUrl(url);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (displayRoleSelector)
        {
            if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
            {
                var allowedRoles = new RoleMenuProperties($"{InteractionConstants.WhoKnowsAlbumRolePicker}:{databaseAlbum.Id}")
                    .WithPlaceholder(context.Localize("album.whoknows.rolePickerPlaceholder"))
                    .WithMinValues(0)
                    .WithMaxValues(25);

                response.RoleMenu = allowedRoles;
            }
            else
            {
                //response.Components = new ActionRowProperties().WithButton(Constants.GetPremiumServer, disabled: true, customId: "1");
            }
        }

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowAlbumAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string albumValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        if (context.ContextUser.Friends?.Any() != true)
        {
            response.Embed.WithDescription(context.Localize("album.whoknows.noFriendsFound",
                ("command", $"{context.Prefix}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}")));
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, albumValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedAlbums: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var databaseAlbum = await this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);
        var albumName = $"{albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}";

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        var usersWithAlbum = await this._whoKnowsAlbumService.GetFriendUsersForAlbum(context.DiscordGuild, guildUsers,
            guild?.GuildId ?? 0, context.ContextUser.UserId, databaseAlbum.Id);

        usersWithAlbum = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, context.ContextUser,
            albumName, context.DiscordGuild, albumSearch.Album.UserPlaycount);

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        if (albumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnail(albumCoverUrl);
            }
            else
            {
                albumCoverUrl = null;
            }

            var accentColor = await this._albumService.GetAccentColorWithAlbum(context,
                albumCoverUrl, databaseAlbum?.Id, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            response.Embed.WithColor(accentColor);
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows(context.Localize("album.whoknows.imageHeader"),
                context.Localize("album.whoknows.imageFromFriends", ("user", userTitle)),
                albumCoverUrl, albumName,
                usersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"friends-whoknow-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = context.Localize("album.whoknows.friendsTitle",
            ("album", albumSearch.Album.AlbumName),
            ("artist", albumSearch.Album.ArtistName));

        var footer = "";

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer += "\n" + context.LocalizeCount("album.whoknows.hiddenFriends", amountOfHiddenFriends);
        }

        if (usersWithAlbum.Any() && usersWithAlbum.Count > 1)
        {
            var globalListeners = usersWithAlbum.Count;
            var globalPlaycount = usersWithAlbum.Sum(a => a.Playcount);
            var avgPlaycount = usersWithAlbum.Average(a => a.Playcount);

            footer += "\n" + context.Localize("album.whoknows.friendsStats",
                ("listeners", context.LocalizeCount("shared.listeners", globalListeners)),
                ("plays", context.LocalizeCount("shared.plays", globalPlaycount)),
                ("avg", ((int)avgPlaycount).Format(context.NumberFormat)));
        }

        footer += "\n" + context.Localize("album.whoknows.friendsFooter", ("user", userTitle));

        var closeFriendUserIds = await this._friendsService.GetCloseFriendUserIdsAsync(context.ContextUser);

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(usersWithAlbum,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer, closeFriendUserIds: closeFriendUserIds);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(usersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server,
                context.NumberFormat, closeFriendUserIds: closeFriendUserIds);
        if (usersWithAlbum.Count == 0)
        {
            serverUsers = context.Localize("album.whoknows.noFriendListeners");
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (Uri.IsWellFormedUriString(albumSearch.Album.AlbumUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(albumSearch.Album.AlbumUrl);
        }

        response.EmbedFooter.WithText(footer);
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> GlobalWhoKnowsAlbumAsync(
        ContextModel context,
        WhoKnowsSettings settings,
        string albumValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, albumValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedAlbums: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var databaseAlbum = await this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);

        var albumName = $"{albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}";

        var usersWithAlbum = await this._whoKnowsAlbumService.GetGlobalUsersForAlbum(context.DiscordGuild,
            databaseAlbum.Id);

        var filteredUsersWithAlbum =
            await this._whoKnowsService.FilterGlobalUsersAsync(usersWithAlbum, settings.QualityFilterDisabled);

        filteredUsersWithAlbum = await WhoKnowsService.AddOrReplaceUserToIndexList(filteredUsersWithAlbum,
            context.ContextUser, albumName, context.DiscordGuild, albumSearch.Album.UserPlaycount);

        var privacyLevel = PrivacyLevel.Global;

        if (context.DiscordGuild != null)
        {
            var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            filteredUsersWithAlbum =
                WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guildUsers);

            if (settings.AdminView)
            {
                privacyLevel = PrivacyLevel.Server;
            }
        }

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        if (albumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnail(albumCoverUrl);
            }
            else
            {
                albumCoverUrl = null;
            }

            var accentColor = await this._albumService.GetAccentColorWithAlbum(context,
                albumCoverUrl, databaseAlbum?.Id, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            response.Embed.WithColor(accentColor);
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows(context.Localize("album.whoknows.imageHeader"),
                context.Localize("album.whoknows.imageGlobal"),
                albumCoverUrl, albumName,
                filteredUsersWithAlbum, context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"global-whoknows-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = context.Localize("album.whoknows.globalTitle",
            ("album", albumSearch.Album.AlbumName),
            ("artist", albumSearch.Album.ArtistName));

        var footer = new StringBuilder();

        footer = WhoKnowsService.GetGlobalWhoKnowsFooter(footer, settings, context);

        if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
        {
            var globalListeners = filteredUsersWithAlbum.Count;
            var globalPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
            var avgPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

            footer.AppendLine(context.Localize("album.whoknows.globalStats",
                ("listeners", context.LocalizeCount("shared.listeners", globalListeners)),
                ("plays", context.LocalizeCount("shared.plays", globalPlaycount)),
                ("avg", ((int)avgPlaycount).Format(context.NumberFormat))));
        }

        var closeFriendUserIds = await this._friendsService.GetCloseFriendUserIdsAsync(context.ContextUser);

        if (settings.ResponseMode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithAlbum,
                context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                title, footer.ToString(), hidePrivateUsers: settings.HidePrivateUsers,
                closeFriendUserIds: closeFriendUserIds);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, context.ContextUser.UserId,
            privacyLevel, context.NumberFormat, hidePrivateUsers: settings.HidePrivateUsers,
            closeFriendUserIds: closeFriendUserIds);
        if (filteredUsersWithAlbum.Count == 0)
        {
            serverUsers = context.Localize("album.whoknows.noGlobalListeners");
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        var url = context.ContextUser.RymEnabled == true
            ? StringExtensions.GetRymUrl(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName)
            : albumSearch.Album.AlbumUrl;

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            response.Embed.WithUrl(url);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> GuildAlbumsAsync(
        ContextModel context,
        Guild guild,
        GuildRankingSettings guildListSettings,
        List<ulong> roles = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        int[] roleUserIds = null;
        if (roles != null && roles.Any())
        {
            var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);
            roleUserIds = guildUsers.Values
                .Where(w => w.Roles != null && w.Roles.Any(roles.Contains))
                .Select(s => s.UserId)
                .ToArray();
        }

        ICollection<GuildAlbum> topGuildAlbums;
        IList<GuildAlbum> previousTopGuildAlbums = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildAlbums = await this._whoKnowsAlbumService.GetTopAllTimeAlbumsForGuild(guild.GuildId,
                guildListSettings.OrderType, guildListSettings.NewSearchValue, userIds: roleUserIds);
        }
        else
        {
            topGuildAlbums = await this._playService.GetGuildTopAlbumsPlays(guild.GuildId,
                guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue, guildListSettings.EndDateTime,
                userIds: roleUserIds);
            previousTopGuildAlbums = (await this._playService.GetGuildTopAlbumsPlays(guild.GuildId,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue,
                guildListSettings.BillboardEndDateTime, userIds: roleUserIds)).ToList();
        }

        if (!topGuildAlbums.Any() && (roles == null || !roles.Any()))
        {
            response.Embed.WithDescription(guildListSettings.NewSearchValue != null
                ? context.Localize("album.server.noResultsSearch", ("artist", guildListSettings.NewSearchValue))
                : context.Localize("album.server.noResults"));
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue)
            ? context.Localize("album.server.title",
                ("period", context.Localizer.PeriodLabel(guildListSettings)),
                ("server", context.DiscordGuild.Name))
            : context.Localize("album.server.titleSearch",
                ("period", context.Localizer.PeriodLabel(guildListSettings)),
                ("search", guildListSettings.NewSearchValue),
                ("server", context.DiscordGuild.Name));

        var footerLabel = guildListSettings.OrderType == OrderType.Listeners
            ? context.Localize("server.orderListeners")
            : context.Localize("server.orderPlays");

        string footerHint = new Random().Next(0, 5) switch
        {
            1 => context.Localize("server.hintWhoKnowsAlbum", ("command", $"{context.Prefix}whoknowsalbum")),
            2 => context.Localize("server.hintTimePeriods"),
            3 => context.Localize("server.hintSorting"),
            _ => null
        };

        var albumPages = topGuildAlbums.Chunk(12).ToList();

        var counter = 1;
        var pageDescriptions = new List<string>();
        foreach (var page in albumPages)
        {
            var pageString = new StringBuilder();
            foreach (var album in page)
            {
                var albumName = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue)
                    ? $"**{StringExtensions.Sanitize(album.ArtistName)}** - **{StringExtensions.Sanitize(album.AlbumName)}**"
                    : $"**{StringExtensions.Sanitize(album.AlbumName)}**";
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{album.ListenerCount.Format(context.NumberFormat)}` · {albumName} · *{context.LocalizeCount("shared.plays", album.TotalPlaycount)}*"
                    : $"`{album.TotalPlaycount.Format(context.NumberFormat)}` · {albumName} · *{context.LocalizeCount("shared.listeners", album.ListenerCount)}*";

                if (previousTopGuildAlbums != null && previousTopGuildAlbums.Any())
                {
                    var previousTopAlbum = previousTopGuildAlbums.FirstOrDefault(f =>
                        f.ArtistName == album.ArtistName && f.AlbumName == album.AlbumName);
                    int? previousPosition = previousTopAlbum == null
                        ? null
                        : previousTopGuildAlbums.IndexOf(previousTopAlbum);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false)
                        .Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            pageDescriptions.Add(pageString.ToString());
        }

        if (pageDescriptions.Count == 0)
        {
            pageDescriptions.Add(context.Localize("album.server.noResultsRoles"));
        }

        RoleMenuProperties roleMenu = null;
        if (guildListSettings.DisplayRoleFilter &&
            PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            roleMenu = new RoleMenuProperties(
                    $"{InteractionConstants.ServerAlbumsRolePicker}:{(int)guildListSettings.OrderType}:{guildListSettings.TimeDescription}:{guildListSettings.NewSearchValue}")
                .WithPlaceholder(context.Localize("server.roleFilterPlaceholder"))
                .WithMinValues(0)
                .WithMaxValues(25);
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;
        response.ResponseType = ResponseType.Paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
            }

            container.WithSeparator();

            var pageFooter = $"-# {footerLabel} - {context.Localize("shared.pageCounter", ("page", (p.CurrentPageIndex + 1).ToString()), ("pages", pageDescriptions.Count.ToString()))}";
            if (roles != null && roles.Any())
            {
                pageFooter += $"\n-# {context.LocalizeCount("server.roleFilterEnabled", roles.Count)}";
            }
            if (footerHint != null)
            {
                pageFooter += $"\n-# {footerHint}";
            }

            container.WithTextDisplay(pageFooter);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            if (roleMenu != null)
            {
                container.AddComponent(roleMenu);
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    public async Task<ResponseModel> AlbumTracksAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool orderByPlaycount = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            albumSearch.Response.ResponseType = ResponseType.ComponentsV2;
            albumSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return albumSearch.Response;
        }

        var albumName = $"{albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}";

        var spotifySource = false;

        List<AlbumTrack> albumTracks;
        var dbAlbum = await this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);

        if (albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Any())
        {
            albumTracks = albumSearch.Album.AlbumTracks;
        }
        else
        {
            dbAlbum.Tracks = await this._spotifyService.GetDatabaseAlbumTracks(dbAlbum.Id);

            if (dbAlbum?.Tracks != null && dbAlbum.Tracks.Count != 0)
            {
                albumTracks = dbAlbum.Tracks.Select(s => new AlbumTrack
                {
                    TrackName = s.Name,
                    ArtistName = albumSearch.Album.ArtistName,
                    DurationSeconds = s.DurationMs / 1000
                }).ToList();
                spotifySource = true;
            }
            else
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    $"Sorry, but neither Last.fm nor Spotify know the tracks for {albumName}.");
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }
        }

        var artistUserTracks =
            await this._trackService.GetArtistUserTracks(userSettings.UserId, albumSearch.Album.ArtistName);

        var description = new StringBuilder();
        var amountOfDiscs = albumTracks.Count(c => c.Rank == 1) == 0 ? 1 : albumTracks.Count(c => c.Rank == 1);

        var pageDescriptions = new List<string>();

        var footer = new StringBuilder();

        if (orderByPlaycount)
        {
            footer.AppendLine("Ordered by plays");
        }

        footer.Append($"{albumTracks.Count} total tracks");

        if (albumTracks.All(a => a.DurationSeconds.HasValue))
        {
            var totalLength = TimeSpan.FromSeconds(albumTracks.Sum(s => s.DurationSeconds ?? 0));
            var formattedTrackLength =
                $"{(totalLength.Hours == 0 ? $"{totalLength.Minutes}" : $"{totalLength.Hours}:{totalLength.Minutes:D2}")}:{totalLength.Seconds:D2}";
            footer.Append($" — {formattedTrackLength}");
        }

        footer.AppendLine();
        footer.Append(spotifySource ? "Album source: Spotify | " : "Album source: Last.fm | ");
        footer.Append(
            $"{userSettings.DisplayName} has {albumSearch.Album.UserPlaycount} total album plays");

        var i = 0;
        var tracksDisplayed = 0;

        foreach (var albumTrack in albumTracks)
        {
            var albumTrackWithPlaycount = artistUserTracks.FirstOrDefault(f =>
                StringExtensions.SanitizeTrackNameForComparison(albumTrack.TrackName)
                    .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));
            if (albumTrackWithPlaycount != null)
            {
                albumTrack.Playcount = albumTrackWithPlaycount.Playcount;
            }
        }

        if (orderByPlaycount)
        {
            amountOfDiscs = 1;
            albumTracks = albumTracks.OrderByDescending(o => o.Playcount).ToList();
        }

        for (var disc = 1; disc < amountOfDiscs + 1; disc++)
        {
            if (amountOfDiscs > 1)
            {
                description.AppendLine($"`Disc {disc}`");
            }

            for (; i < albumTracks.Count; i++)
            {
                var albumTrack = albumTracks[i];

                description.Append($"{i + 1}.");
                description.Append($" **{albumTrack.TrackName}**");

                if (albumTrack.Playcount.HasValue)
                {
                    description.Append(
                        $" - *{albumTrack.Playcount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(albumTrack.Playcount)}*");
                }

                if (albumTrack.DurationSeconds.HasValue)
                {
                    description.Append(!albumTrack.Playcount.HasValue ? " — " : " - ");

                    var duration = TimeSpan.FromSeconds(albumTrack.DurationSeconds.Value);
                    var formattedTrackLength =
                        $"{(duration.Hours == 0 ? "" : $"{duration.Hours}:")}{duration.Minutes}:{duration.Seconds:D2}";
                    description.Append($"`{formattedTrackLength}`");
                }

                description.AppendLine();

                tracksDisplayed++;
                if (tracksDisplayed > 0 && tracksDisplayed % 12 == 0 || tracksDisplayed == albumTracks.Count)
                {
                    pageDescriptions.Add(description.ToString());
                    description = new StringBuilder();
                }
            }
        }

        dbAlbum ??= await this._albumService.GetAlbumFromDatabase(albumSearch.Album.ArtistName,
            albumSearch.Album.AlbumName);

        var albumInfoId =
            $"{InteractionConstants.Album.Info}:{dbAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}";
        var albumCoverId =
            $"{InteractionConstants.Album.Cover}:{dbAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:motion:";

        var title = $"### Track playcounts for {albumName}";
        var footerText = footer.ToString().TrimEnd();

        if (pageDescriptions.Count == 1)
        {
            response.ResponseType = ResponseType.ComponentsV2;

            response.ComponentsContainer.WithTextDisplay(title);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay(pageDescriptions[0].TrimEnd());
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay($"-# {footerText.Replace("\n", "\n-# ")}");

            var actionRow = new ActionRowProperties()
                .WithButton("Album", albumInfoId,
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("💽"))
                .WithButton("Cover", albumCoverId,
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("🖼️"));
            response.ComponentsContainer.WithActionRow(actionRow);
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;

            var paginator = new ComponentPaginatorBuilder()
                .WithPageFactory(GeneratePage)
                .WithPageCount(Math.Max(1, pageDescriptions.Count))
                .WithActionOnTimeout(ActionOnStop.DisableInput);

            response.ComponentPaginator = paginator;

            IPage GeneratePage(IComponentPaginator p)
            {
                var container = new ComponentContainerProperties();

                container.WithTextDisplay(title);
                container.WithSeparator();

                var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
                if (currentPage != null)
                {
                    container.WithTextDisplay(currentPage.TrimEnd());
                }

                container.WithSeparator();
                container.WithTextDisplay($"-# Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} — {footerText.Replace("\n", "\n-# ")}");

                if (pageDescriptions.Count > 1)
                {
                    var actionRow = new ActionRowProperties()
                        .WithButton("Album", albumInfoId, style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("💽"))
                        .WithButton("Cover", albumCoverId, style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("🖼️"))
                        .AddPreviousButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious))
                        .AddNextButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesNext));
                    container.WithActionRow(actionRow);
                }

                return new PageBuilder()
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithMessageFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([container])
                    .Build();
            }
        }

        return response;
    }

    public async Task<ResponseModel> AlbumPlaysAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            otherUserUsername: userSettings.UserNameLastFm, userId: context.ContextUser.UserId,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var reply = context.LocalizeCount("album.plays.userPlays",
            albumSearch.Album.UserPlaycount.GetValueOrDefault(),
            ("user", $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}"),
            ("album", StringExtensions.Sanitize(albumSearch.Album.AlbumName)),
            ("artist", StringExtensions.Sanitize(albumSearch.Album.ArtistName)));

        if (albumSearch.Album.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserAlbumPlaycount(context.ContextUser.UserId,
                albumSearch.Album.ArtistName,
                albumSearch.Album.AlbumName, albumSearch.Album.UserPlaycount.Value);
        }

        if (userSettings.DifferentUser)
        {
            await this._updateService.UpdateUser(new UpdateUserQueueItem(userSettings.UserId));
        }

        var recentAlbumPlaycounts =
            await this._playService.GetRecentAlbumPlaycounts(userSettings.UserId, albumSearch.Album.AlbumName,
                albumSearch.Album.ArtistName);
        if (recentAlbumPlaycounts.month != 0)
        {
            reply += $"\n{context.Localize("shared.recentWeekMonthPlays",
                ("week", context.LocalizeCount("shared.plays", recentAlbumPlaycounts.week)),
                ("month", context.LocalizeCount("shared.plays", recentAlbumPlaycounts.month)))}";
        }


        response.Text = reply;

        return response;
    }

    public async Task<ResponseModel> CoverAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool? motionCover = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, context.Localizer, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            useCachedAlbums: false, userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            albumSearch.Response.ResponseType = ResponseType.ComponentsV2;
            albumSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return albumSearch.Response;
        }

        var databaseAlbum = await this._musicDataFactory.GetOrStoreAlbumAsync(albumSearch.Album);

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        var albumImages = await this._albumService.GetAlbumImages(databaseAlbum.Id);

        var staticAlbumCoverUrl = albumCoverUrl;

        var actionRow = new ActionRowProperties()
            .WithButton("Album",
                $"{InteractionConstants.Album.Info}:{databaseAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("💽"))
            .WithButton("Tracks",
                $"{InteractionConstants.Album.Tracks}:{databaseAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("🎶"));

        if (albumSearch.IsRandom)
        {
            actionRow.WithButton("Reroll",
                $"{InteractionConstants.Album.RandomCover}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("🎲"));
        }

        var showMotionCover = motionCover ?? context.ContextUser.CoverType != CoverType.Still;

        var gifResult = false;
        if (showMotionCover && albumImages.Any(a => a.ImageType == ImageType.VideoSquare))
        {
            albumCoverUrl = albumImages.First(f => f.ImageType == ImageType.VideoSquare).Url;
            gifResult = true;
            actionRow.WithButton("Still",
                $"{InteractionConstants.Album.Cover}:{databaseAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:still:",
                ButtonStyle.Secondary, EmojiProperties.Standard("🖼️"));
        }
        else if (albumImages.Any(a => a.ImageType == ImageType.VideoSquare))
        {
            actionRow.WithButton("Motion",
                $"{InteractionConstants.Album.Cover}:{databaseAlbum.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:motion:",
                ButtonStyle.Secondary, EmojiProperties.Standard("▶️"));
        }

        if (albumCoverUrl == null)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                $"Sorry, no album cover found for this album: \n{albumSearch.Album.ArtistName} - {albumSearch.Album.AlbumName}\n[View on last.fm]({albumSearch.Album.AlbumUrl})");
            response.ComponentsContainer.WithActionRow(actionRow);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
            albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl, response.Embed);

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(response.Embed.Description ?? "Sorry, this album or artist can't be posted due to it possibly violating Discord ToS.");
            response.ComponentsContainer.WithActionRow(actionRow);
            response.CommandResponse = CommandResponse.Censored;
            return response;
        }

        response.Spoiler = safeForChannel == CensorService.CensorResult.Nsfw;
        response.FileDescription = StringExtensions.TruncateLongString($"Album cover for {albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}", 512);

        if (gifResult)
        {
            var cacheFilePath =
                ChartService.AlbumUrlToCacheFilePath(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                    ".webp");

            Stream gifStream;
            if (File.Exists(cacheFilePath))
            {
                var memoryStream = new MemoryStream();
                await using (var fileStream = File.OpenRead(cacheFilePath))
                {
                    await fileStream.CopyToAsync(memoryStream);
                }

                memoryStream.Position = 0;
                gifStream = memoryStream;
            }
            else
            {
                var specificUrl = await this._appleMusicVideoService.GetVideoUrlFromM3U8(albumCoverUrl);
                gifStream = await AppleMusicVideoService.ConvertM3U8ToWebPAsync(specificUrl);
                try
                {
                    await ChartService.OverwriteCache(gifStream, cacheFilePath, SKEncodedImageFormat.Webp);
                }
                catch (IOException)
                {
                    // Cache write failed due to concurrent access, not critical
                }

                gifStream.Position = 0;
            }

            response.Stream = gifStream;
            response.FileName = StringExtensions.ReplaceInvalidChars(
                $"cover-{albumSearch.Album.ArtistName}_{albumSearch.Album.AlbumName}.webp");
        }
        else
        {
            var image = await this._dataSourceFactory.GetAlbumImageAsStreamAsync(albumCoverUrl);
            if (image == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    $"Sorry, something went wrong while getting album cover for this album: \n{albumSearch.Album.ArtistName} - {albumSearch.Album.AlbumName}\n[View on last.fm]({albumSearch.Album.AlbumUrl})");
                response.ComponentsContainer.WithActionRow(actionRow);
                response.CommandResponse = CommandResponse.Error;
                return response;
            }

            var cacheStream = new MemoryStream();
            await image.CopyToAsync(cacheStream);
            image.Position = 0;

            response.Stream = image;
            response.FileName = StringExtensions.ReplaceInvalidChars(
                $"cover-{albumSearch.Album.ArtistName}_{albumSearch.Album.AlbumName}.png");

            var cacheFilePath =
                ChartService.AlbumUrlToCacheFilePath(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            await ChartService.OverwriteCache(cacheStream, cacheFilePath);
            await cacheStream.DisposeAsync();
        }

        var accentColor = await this._albumService.GetAccentColorWithAlbum(context,
            staticAlbumCoverUrl, databaseAlbum?.Id, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

        response.ComponentsContainer.WithAccentColor(accentColor);

        var mediaGallery = new MediaGalleryItemProperties(
            new ComponentMediaProperties($"attachment://{response.FileName}"))
        {
            Description = StringExtensions.TruncateLongString(response.FileDescription, 256),
            Spoiler = response.Spoiler
        };
        response.ComponentsContainer.AddComponent(new MediaGalleryProperties { mediaGallery });

        var descriptionText = new StringBuilder();
        descriptionText.AppendLine(
            $"**{StringExtensions.MarkdownLink(albumSearch.Album.ArtistName, albumSearch.Album.ArtistUrl)} - {StringExtensions.MarkdownLink(albumSearch.Album.AlbumName, albumSearch.Album.AlbumUrl)}**");

        if (safeForChannel == CensorService.CensorResult.Nsfw)
        {
            descriptionText.AppendLine(context.Localize("artist.whoknows.nsfwReveal"));
        }

        descriptionText.Append(
            $"-# {context.Localize("shared.requestedBy", ("user", await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)))}");

        if (albumSearch.IsRandom)
        {
            descriptionText.AppendLine();
            descriptionText.Append(
                $"-# {context.LocalizeCount("album.randomPositionCover", albumSearch.RandomAlbumPlaycount.GetValueOrDefault(), ("position", albumSearch.RandomAlbumPosition.Format(context.NumberFormat)))}");
        }

        response.ComponentsContainer.WithTextDisplay(descriptionText.ToString());
        response.ComponentsContainer.WithActionRow(actionRow);

        return response;
    }

    public async Task<ResponseModel> TopAlbumsAsync(ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings,
        ResponseMode mode)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var pages = new List<PageBuilder>();

        string userTitle;
        string authorName;
        if (!userSettings.DifferentUser)
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
            authorName = context.Localize("album.topAlbumsTitleSelf",
                ("period", context.Localizer.PeriodLabel(timeSettings)),
                ("user", userTitle));
        }
        else
        {
            var requesterTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
            userTitle = context.Localize("album.topAlbumsRequestedBy",
                ("user", userSettings.UserNameLastFm),
                ("requester", requesterTitle));
            authorName = context.Localize("album.topAlbumsTitleOther",
                ("period", context.Localizer.PeriodLabel(timeSettings)),
                ("user", userSettings.UserNameLastFm),
                ("requester", requesterTitle));
        }

        var userUrl = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/albums?{timeSettings.UrlParameter}");

        response.EmbedAuthor.WithName(authorName);
        response.EmbedAuthor.WithUrl(userUrl);

        var amount = topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue || topListSettings.FilterSingles
            ? 1000
            : topListSettings.ListAmount;
        var albums =
            await this._dataSourceFactory.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings, amount,
                useCache: true);

        if (!albums.Success || albums.Content == null)
        {
            response.Embed.ErrorResponse(albums.Error, albums.Message, "top albums", context.Localizer, context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (albums.Content?.TopAlbums == null || !albums.Content.TopAlbums.Any())
        {
            response.Embed.WithDescription(context.Localize("album.topAlbumsNoResults", ("url", userUrl)));
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if ((topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue) &&
            timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var topAllTimeDb = topListSettings.ReleaseYearFilter.HasValue
                ? await this._albumService.GetUserAllTimeTopAlbumsByReleaseYear(userSettings.UserId,
                    topListSettings.ReleaseYearFilter.Value)
                : await this._albumService.GetUserAllTimeTopAlbumsByReleaseDecade(userSettings.UserId,
                    topListSettings.ReleaseDecadeFilter.Value);

            albums.Content.TopAlbums = topAllTimeDb;
        }

        if (topListSettings.ReleaseYearFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseYear(albums, topListSettings.ReleaseYearFilter.Value);
        }
        else if (topListSettings.ReleaseDecadeFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseDecade(albums,
                topListSettings.ReleaseDecadeFilter.Value);
        }

        if (topListSettings.FilterSingles)
        {
            albums = await this._albumService.FilterAlbumsThatAreSingles(albums);
        }

        if (mode == ResponseMode.Image)
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            albums.Content.TopAlbums = await this._albumService.FillMissingAlbumCovers(albums.Content.TopAlbums);

            var firstAlbumImage =
                albums.Content.TopAlbums.FirstOrDefault(f => f.AlbumCoverUrl != null)?.AlbumCoverUrl;

            string title;
            if (topListSettings.ReleaseYearFilter.HasValue)
            {
                title = topListSettings.FilterSingles
                    ? context.Localize("album.topAlbumsImageTitleYearSingles",
                        ("year", topListSettings.ReleaseYearFilter.Value.ToString()))
                    : context.Localize("album.topAlbumsImageTitleYear",
                        ("year", topListSettings.ReleaseYearFilter.Value.ToString()));
            }
            else if (topListSettings.ReleaseDecadeFilter.HasValue)
            {
                title = topListSettings.FilterSingles
                    ? context.Localize("album.topAlbumsImageTitleDecadeSingles",
                        ("decade", topListSettings.ReleaseDecadeFilter.Value.ToString()))
                    : context.Localize("album.topAlbumsImageTitleDecade",
                        ("decade", topListSettings.ReleaseDecadeFilter.Value.ToString()));
            }
            else
            {
                title = topListSettings.FilterSingles
                    ? context.Localize("album.topAlbumsImageTitleSingles")
                    : context.Localize("album.topAlbumsImageTitle");
            }

            using var image = await this._puppeteerService.GetTopList(userTitle, title, "albums", timeSettings.Description,
                albums.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), firstAlbumImage,
                albums.TopList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-albums-{userSettings.UserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var previousTopAlbums = new List<TopAlbum>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousAlbumsCall = await this._dataSourceFactory
                .GetTopAlbumsForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, amount);

            if (previousAlbumsCall.Success)
            {
                previousTopAlbums.AddRange(previousAlbumsCall.Content.TopAlbums);
            }
        }

        var albumPages = albums.Content.TopAlbums.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var albumPage in albumPages)
        {
            var albumPageString = new StringBuilder();
            foreach (var album in albumPage)
            {
                var url = album.AlbumUrl;
                var escapedAlbumName = Regex.Replace(album.AlbumName, @"([|\\*])", @"\$1");

                if (context.ContextUser.RymEnabled == true)
                {
                    url = StringExtensions.GetRymUrl(album.AlbumName, album.ArtistName);
                }

                var name =
                    $"**{album.ArtistName}** - **{StringExtensions.MarkdownLink(escapedAlbumName, url)}** - *{context.LocalizeCount("shared.plays", album.UserPlaycount.GetValueOrDefault())}*";

                if (topListSettings.Billboard && previousTopAlbums.Any())
                {
                    var previousTopAlbum = previousTopAlbums.FirstOrDefault(f =>
                        f.ArtistName == album.ArtistName && f.AlbumName == album.AlbumName);
                    int? previousPosition =
                        previousTopAlbum == null ? null : previousTopAlbums.IndexOf(previousTopAlbum);

                    albumPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition)
                        .Text);
                }
                else
                {
                    albumPageString.Append($"{counter}. ");
                    albumPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();

            ImportService.AddImportDescription(footer, albums.PlaySources);

            if (albums.Content.TotalAmount.HasValue && albums.Content.TotalAmount.Value != amount)
            {
                footer.Append(context.Localize("album.topAlbumsPageCounterTotal",
                    ("page", pageCounter.ToString()),
                    ("pages", albumPages.Count.ToString()),
                    ("amount", albums.Content.TotalAmount.Value.Format(context.NumberFormat))));
            }
            else
            {
                footer.Append(context.Localize("shared.pageCounter",
                    ("page", pageCounter.ToString()),
                    ("pages", albumPages.Count.ToString())));
            }

            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (topListSettings.ReleaseYearFilter.HasValue)
            {
                footer.AppendLine();
                footer.Append(context.Localize("album.topAlbumsFilterYear",
                    ("year", topListSettings.ReleaseYearFilter.Value.ToString())));
            }

            if (topListSettings.ReleaseDecadeFilter.HasValue)
            {
                footer.AppendLine();
                footer.Append(context.Localize("album.topAlbumsFilterDecade",
                    ("decade", topListSettings.ReleaseDecadeFilter.Value.ToString())));
            }

            if (topListSettings.FilterSingles)
            {
                footer.AppendLine();
                footer.Append(context.Localize("album.topAlbumsFilterSingles"));
            }

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine();
                footer.Append(context.Localize("album.topAlbumsBillboardHint"));
            }

            pages.Add(new PageBuilder()
                .WithDescription(albumPageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription(context.Localize("album.topAlbumsNoPlays"))
                .WithAuthor(response.EmbedAuthor));
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }
}
