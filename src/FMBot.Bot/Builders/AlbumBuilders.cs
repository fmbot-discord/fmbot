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
        MusicDataFactory musicDataFactory, AppleMusicVideoService appleMusicVideoService)
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
    }

    public async Task<ResponseModel> AlbumAsync(ContextModel context,
        string searchValue, UserSettingsModel userSettings = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue,
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

        Guild guild = null;
        IDictionary<int, FullGuildUser> guildUsers = null;
        Task<IList<WhoKnowsObjectWithUser>> indexedUsersTask = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
            guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            if (guild?.LastIndexed != null)
            {
                indexedUsersTask = this._whoKnowsAlbumService.GetIndexedUsersForAlbum(context.DiscordGuild,
                    guildUsers, guild.GuildId, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
            }
        }

        var databaseAlbum = await databaseAlbumTask;
        var userTitle = await userTitleTask;
        var artistUserTracks = await artistUserTracksTask;
        var featuredHistory = await featuredHistoryTask;

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
        var albumType = databaseAlbum?.Type switch
        {
            "single" => "Single",
            "compilation" => "Compilation",
            _ => "Album"
        };

        var headerSection = new StringBuilder();
        headerSection.AppendLine(albumSearch.Album.AlbumUrl != null
            ? $"## [{albumSearch.Album.AlbumName}]({albumSearch.Album.AlbumUrl})"
            : $"## {albumSearch.Album.AlbumName}");
        headerSection.AppendLine(albumSearch.Album.ArtistUrl != null
            ? $"{albumType} by **[{albumSearch.Album.ArtistName}]({albumSearch.Album.ArtistUrl})**"
            : $"{albumType} by **{albumSearch.Album.ArtistName}**");

        if (databaseAlbum.ReleaseDate != null)
        {
            headerSection.AppendLine($"Released on **{AlbumService.GetAlbumReleaseDate(databaseAlbum)}**");
        }

        if (databaseAlbum?.Label != null)
        {
            headerSection.Append($"-# Label: {databaseAlbum.Label}");
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
            if (context.ContextUser.UserType != UserType.User && albumSearch.Album.UserPlaycount > 0)
            {
                firstPlayTask = this._playService.GetAlbumFirstPlayDate(context.ContextUser.UserId,
                    albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
            }

            await correctPlaycountTask;
            var recentPlaycounts = await recentPlaycountsTask;
            var userStats = new StringBuilder();

            var playsLine =
                $"**{albumSearch.Album.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(albumSearch.Album.UserPlaycount)} by **{userTitle}**";
            if (recentPlaycounts.month > 0)
            {
                playsLine += $" — **{recentPlaycounts.month.Format(context.NumberFormat)}** last month";
            }

            userStats.AppendLine(playsLine);

            if (albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Count != 0 && artistUserTracks.Count != 0)
            {
                var listeningTime = await this._timeService.GetAllTimePlayTimeForAlbum(albumSearch.Album.AlbumTracks,
                    artistUserTracks, albumSearch.Album.UserPlaycount.Value);
                userStats.Append($"**{StringExtensions.GetLongListeningTimeString(listeningTime)}** listened");

                if (context.ContextUser.TotalPlaycount.HasValue && albumSearch.Album.UserPlaycount is >= 30)
                {
                    userStats.Append(
                        $" — **{((decimal)albumSearch.Album.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat)}** of all your plays");
                }
                userStats.AppendLine();

                if (albumSearch.Album.AlbumTracks.Count > 1 && SupporterService.IsSupporter(userSettings?.UserType))
                {
                    var tracksHeard = albumSearch.Album.AlbumTracks.Count(t =>
                        artistUserTracks.Any(ut => StringExtensions.SanitizeTrackNameForComparison(t.TrackName)
                            .Equals(StringExtensions.SanitizeTrackNameForComparison(ut.Name))));
                    userStats.AppendLine($"**{tracksHeard}/{albumSearch.Album.AlbumTracks.Count}** tracks listened");
                }
            }

            if (firstPlayTask != null)
            {
                var firstPlay = await firstPlayTask;
                if (firstPlay != null)
                {
                    var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();
                    userStats.AppendLine($"Discovered <t:{firstListenValue}:D>");
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
                    userStats.AppendLine(
                        $"*[Supporters]({Constants.GetSupporterOverviewLink}) can see album discovery dates.*");
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

                    statsSection.AppendLine(
                        $"**{serverPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(serverPlaycount)} in this server by **{serverListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(serverListeners)}");
                }
            }

            var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingAlbum(context.ContextUser.UserId,
                guildUsers, guild, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

            if (guildAlsoPlaying != null)
            {
                statsSection.AppendLine(guildAlsoPlaying);
            }
        }

        statsSection.AppendLine(
            $"**{albumSearch.Album.TotalPlaycount.Format(context.NumberFormat)}** Last.fm {StringExtensions.GetPlaysString(albumSearch.Album.TotalPlaycount)} by **{albumSearch.Album.TotalListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(albumSearch.Album.TotalListeners)}");

        var metaLine = new StringBuilder();
        if (databaseAlbum?.Popularity is > 0)
        {
            metaLine.Append($"**{databaseAlbum.Popularity}** popularity");
        }

        if (featuredHistory.Any())
        {
            if (metaLine.Length > 0) metaLine.Append(" — ");
            metaLine.Append($"Featured **{featuredHistory.Count}** {StringExtensions.GetTimesString(featuredHistory.Count)}");
        }

        if (metaLine.Length > 0)
        {
            statsSection.AppendLine(metaLine.ToString());
        }

        if (albumSearch.IsRandom)
        {
            statsSection.AppendLine(
                $"Album #{albumSearch.RandomAlbumPosition} ({albumSearch.RandomAlbumPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(albumSearch.RandomAlbumPlaycount)})");
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
                "Tracks",
                $"{InteractionConstants.Album.Tracks}:{databaseAlbum.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Standard("🎶"))
            .WithButton(
                "Cover",
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
        List<ulong> roles = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, albumValues,
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
            guild.GuildId, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId);
        var currentUser =
            await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);

        usersWithAlbum = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithAlbum, context.ContextUser,
            fullAlbumName, context.DiscordGuild, albumSearch.Album.UserPlaycount);

        var (filterStats, filteredUsersWithAlbum) =
            WhoKnowsService.FilterWhoKnowsObjects(usersWithAlbum, guildUsers, guild, context.ContextUser.UserId, roles);

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
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Album",
                $"in <b>{context.DiscordGuild.Name}</b>", albumCoverUrl, fullAlbumName,
                filteredUsersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"whoknows-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = StringExtensions.TruncateLongString($"{fullAlbumName} in {context.DiscordGuild.Name}", 255);

        var footer = new StringBuilder();

        if (albumSearch.IsRandom)
        {
            footer.AppendLine(
                $"Album #{albumSearch.RandomAlbumPosition} ({albumSearch.RandomAlbumPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(albumSearch.RandomAlbumPlaycount)})");
        }

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.DiscordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
        {
            footer.AppendLine($"Missing members? Update with {context.Prefix}refreshmembers");
        }

        if (filterStats.FullDescription != null)
        {
            footer.AppendLine($"{filterStats.FullDescription}");
        }

        if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
        {
            var serverListeners = filteredUsersWithAlbum.Count;
            var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

            footer.Append($"Album - ");
            footer.Append(
                $"{serverListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append(
                $"{serverPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.AppendLine($"{((int)avgServerPlaycount).Format(context.NumberFormat)} avg");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingAlbum(context.ContextUser.UserId,
            guildUsers, guild, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithAlbum,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString());

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, context.ContextUser.UserId,
                PrivacyLevel.Server, context.NumberFormat);
        if (filteredUsersWithAlbum.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this album.";
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
                    .WithPlaceholder("Apply role filter..")
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
            response.Embed.WithDescription("We couldn't find any friends. To add friends:\n" +
                                           $"`{context.Prefix}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`\n\n" +
                                           $"Or right-click a user, go to apps and click 'Add as friend'");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, albumValues,
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
            guild?.GuildId ?? 0, context.ContextUser.UserId, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

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
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Album", $"from <b>{userTitle}</b>'s friends",
                albumCoverUrl, albumName,
                usersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"friends-whoknow-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = $"{albumName} with friends";

        var footer = "";

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer +=
                $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
        }

        if (usersWithAlbum.Any() && usersWithAlbum.Count > 1)
        {
            var globalListeners = usersWithAlbum.Count;
            var globalPlaycount = usersWithAlbum.Sum(a => a.Playcount);
            var avgPlaycount = usersWithAlbum.Average(a => a.Playcount);

            footer +=
                $"\n{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ";
            footer +=
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ";
            footer += $"{((int)avgPlaycount).Format(context.NumberFormat)} avg";
        }

        footer += $"\nFriends WhoKnow album for {userTitle}";

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(usersWithAlbum,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(usersWithAlbum, context.ContextUser.UserId, PrivacyLevel.Server,
                context.NumberFormat);
        if (usersWithAlbum.Count == 0)
        {
            serverUsers = "None of your friends have listened to this album.";
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

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, albumValues,
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
            albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);

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
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Album", $"in <b>.fmbot 🌐</b>",
                albumCoverUrl, albumName,
                filteredUsersWithAlbum, context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"global-whoknows-album-{albumSearch.Album.ArtistName}-{albumSearch.Album.AlbumName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = $"{albumName} globally";

        var footer = new StringBuilder();

        footer = WhoKnowsService.GetGlobalWhoKnowsFooter(footer, settings, context);

        if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count > 1)
        {
            var globalListeners = filteredUsersWithAlbum.Count;
            var globalPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
            var avgPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

            footer.Append($"Global album - ");
            footer.Append(
                $"{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append(
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{((int)avgPlaycount).Format(context.NumberFormat)} avg");
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithAlbum,
                context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                title, footer.ToString(), hidePrivateUsers: settings.HidePrivateUsers);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, context.ContextUser.UserId,
            privacyLevel, context.NumberFormat, hidePrivateUsers: settings.HidePrivateUsers);
        if (filteredUsersWithAlbum.Count == 0)
        {
            serverUsers = "Nobody that uses .fmbot has listened to this album.";
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
        GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        ICollection<GuildAlbum> topGuildAlbums;
        IList<GuildAlbum> previousTopGuildAlbums = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildAlbums = await this._whoKnowsAlbumService.GetTopAllTimeAlbumsForGuild(guild.GuildId,
                guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            topGuildAlbums = await this._playService.GetGuildTopAlbumsPlays(guild.GuildId,
                guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue, guildListSettings.EndDateTime);
            previousTopGuildAlbums = (await this._playService.GetGuildTopAlbumsPlays(guild.GuildId,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue,
                guildListSettings.BillboardEndDateTime)).ToList();
        }

        if (!topGuildAlbums.Any())
        {
            response.Embed.WithDescription(guildListSettings.NewSearchValue != null
                ? $"Sorry, there are no registered top albums for artist `{guildListSettings.NewSearchValue}` on this server in the time period you selected."
                : $"Sorry, there are no registered top albums on this server in the time period you selected.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue)
            ? $"Top {guildListSettings.TimeDescription.ToLower()} albums in {context.DiscordGuild.Name}"
            : $"Top {guildListSettings.TimeDescription.ToLower()} '{guildListSettings.NewSearchValue}' albums in {context.DiscordGuild.Name}";

        var footerLabel = guildListSettings.OrderType == OrderType.Listeners
            ? "Listener count"
            : "Play count";

        string footerHint = new Random().Next(0, 5) switch
        {
            1 => $"View specific album listeners with '{context.Prefix}whoknowsalbum'",
            2 => "Available time periods: alltime, monthly, weekly, current and last month",
            3 => "Available sorting options: plays and listeners",
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
                    ? $"**{album.ArtistName}** - **{album.AlbumName}**"
                    : $"**{album.AlbumName}**";
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{album.ListenerCount.Format(context.NumberFormat)}` · {albumName} · *{album.TotalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(album.TotalPlaycount)}*"
                    : $"`{album.TotalPlaycount.Format(context.NumberFormat)}` · {albumName} · *{album.ListenerCount.Format(context.NumberFormat)} {StringExtensions.GetListenersString(album.ListenerCount)}*";

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

            var pageFooter = $"-# {footerLabel} - Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count}";
            if (footerHint != null)
            {
                pageFooter += $"\n-# {footerHint}";
            }

            container.WithTextDisplay(pageFooter);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
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

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue,
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
            $"{userSettings.DisplayName} has {albumSearch.Album.UserPlaycount} total scrobbles on this album");

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

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            otherUserUsername: userSettings.UserNameLastFm, userId: context.ContextUser.UserId,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has **{albumSearch.Album.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(albumSearch.Album.UserPlaycount)} " +
            $"for **{StringExtensions.Sanitize(albumSearch.Album.AlbumName)}** by **{StringExtensions.Sanitize(albumSearch.Album.ArtistName)}**";

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
            reply +=
                $"\n-# *{recentAlbumPlaycounts.week.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentAlbumPlaycounts.week)} last week — " +
                $"{recentAlbumPlaycounts.month.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentAlbumPlaycounts.month)} last month*";
        }


        response.Text = reply;

        return response;
    }

    public async Task<ResponseModel> CoverAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool motionCover = true)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue,
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

        var gifResult = false;
        if (motionCover && albumImages.Any(a => a.ImageType == ImageType.VideoSquare))
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
            $"**[{albumSearch.Album.ArtistName}]({albumSearch.Album.ArtistUrl}) - [{albumSearch.Album.AlbumName}]({albumSearch.Album.AlbumUrl})**");

        if (safeForChannel == CensorService.CensorResult.Nsfw)
        {
            descriptionText.AppendLine("⚠️ NSFW - Click to reveal");
        }

        descriptionText.Append(
            $"-# Requested by {await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)}");

        if (albumSearch.IsRandom)
        {
            descriptionText.AppendLine();
            descriptionText.Append(
                $"-# Random album #{albumSearch.RandomAlbumPosition} ({albumSearch.RandomAlbumPlaycount} {StringExtensions.GetPlaysString(albumSearch.RandomAlbumPlaycount)})");
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
        if (!userSettings.DifferentUser)
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/albums?{timeSettings.UrlParameter}");

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} albums for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var amount = topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue || topListSettings.FilterSingles
            ? 1000
            : topListSettings.ListAmount;
        var albums =
            await this._dataSourceFactory.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings, amount,
                useCache: true);

        if (!albums.Success || albums.Content == null)
        {
            response.Embed.ErrorResponse(albums.Error, albums.Message, "top albums", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (albums.Content?.TopAlbums == null || !albums.Content.TopAlbums.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top albums in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if ((topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue) &&
            timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var topAllTimeDb = await this._albumService.GetUserAllTimeTopAlbums(userSettings.UserId);
            if (topAllTimeDb.Count > 1000)
            {
                albums.Content.TopAlbums = topAllTimeDb;
            }
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

            var title = "Top Albums";
            if (topListSettings.ReleaseYearFilter.HasValue)
            {
                title = $"Top Albums from {topListSettings.ReleaseYearFilter}";
            }
            else if (topListSettings.ReleaseDecadeFilter.HasValue)
            {
                title = $"Top Albums from the {topListSettings.ReleaseDecadeFilter}s";
            }

            if (topListSettings.FilterSingles)
            {
                title += " (excluding singles)";
            }

            using var image = await this._puppeteerService.GetTopList(userTitle, title, "albums", timeSettings.Description,
                albums.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), firstAlbumImage,
                albums.TopList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-albums-{userSettings.DiscordUserId}.png";
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
                    $"**{album.ArtistName}** - **[{escapedAlbumName}]({url})** - *{album.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(album.UserPlaycount)}*";

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

            footer.Append($"Page {pageCounter}/{albumPages.Count}");
            if (albums.Content.TotalAmount.HasValue && albums.Content.TotalAmount.Value != amount)
            {
                footer.Append($" - {albums.Content.TotalAmount} different albums");
            }

            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (topListSettings.ReleaseYearFilter.HasValue)
            {
                footer.AppendLine();
                footer.Append($"Filtering to albums released in {topListSettings.ReleaseYearFilter.Value}");
            }

            if (topListSettings.ReleaseDecadeFilter.HasValue)
            {
                footer.AppendLine();
                footer.Append($"Filtering to albums released in the {topListSettings.ReleaseDecadeFilter.Value}s");
            }

            if (topListSettings.FilterSingles)
            {
                footer.AppendLine();
                footer.Append("Filtering out singles");
            }

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine();
                footer.Append("View as billboard by adding 'billboard' or 'bb'");
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
                .WithDescription("No albums played in this time period.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }
}
