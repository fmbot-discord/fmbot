using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Subscriptions.Services;
using NetCord;
using NetCord.Rest;
using Serilog;
using SkiaSharp;
using Web.InternalApi;

namespace FMBot.Bot.Builders;

public class TrackBuilders
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly TrackService _trackService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly PlayService _playService;
    private readonly SpotifyService _spotifyService;
    private readonly TimeService _timeService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly PuppeteerService _puppeteerService;
    private readonly UpdateService _updateService;
    private readonly IndexService _indexService;
    private readonly CensorService _censorService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly AlbumService _albumService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly DiscogsService _discogsService;
    private readonly ArtistsService _artistsService;
    private readonly FeaturedService _featuredService;
    private readonly MusicDataFactory _musicDataFactory;
    private readonly DiscordSkuService _discordSkuService;
    private readonly SupporterService _supporterService;
    private readonly EurovisionService _eurovisionService;

    public TrackBuilders(UserService userService,
        GuildService guildService,
        TrackService trackService,
        WhoKnowsTrackService whoKnowsTrackService,
        PlayService playService,
        SpotifyService spotifyService,
        TimeService timeService,
        IDataSourceFactory dataSourceFactory,
        PuppeteerService puppeteerService,
        UpdateService updateService,
        SupporterService supporterService,
        IndexService indexService,
        CensorService censorService,
        WhoKnowsService whoKnowsService,
        AlbumService albumService,
        WhoKnowsPlayService whoKnowsPlayService,
        DiscogsService discogsService,
        ArtistsService artistsService,
        FeaturedService featuredService,
        MusicDataFactory musicDataFactory,
        DiscordSkuService discordSkuService, EurovisionService eurovisionService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._trackService = trackService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._playService = playService;
        this._spotifyService = spotifyService;
        this._timeService = timeService;
        this._dataSourceFactory = dataSourceFactory;
        this._puppeteerService = puppeteerService;
        this._updateService = updateService;
        this._indexService = indexService;
        this._censorService = censorService;
        this._whoKnowsService = whoKnowsService;
        this._albumService = albumService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._discogsService = discogsService;
        this._artistsService = artistsService;
        this._featuredService = featuredService;
        this._musicDataFactory = musicDataFactory;
        this._discordSkuService = discordSkuService;
        this._eurovisionService = eurovisionService;
        this._supporterService = supporterService;
    }

    public async Task<ResponseModel> TrackAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            trackSearch.Response.ResponseType = ResponseType.ComponentsV2;
            trackSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return trackSearch.Response;
        }

        var dbTrackTask = this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track);
        var userTitleTask = this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var featuredHistoryTask = this._featuredService.GetTrackFeaturedHistory(trackSearch.Track.ArtistName,
            trackSearch.Track.TrackName);

        Task<Album> databaseAlbumTask = null;
        if (trackSearch.Track.AlbumName != null)
        {
            databaseAlbumTask = this._albumService.GetAlbumFromDatabase(
                trackSearch.Track.ArtistName, trackSearch.Track.AlbumName);
        }

        Task<DateTime?> firstPlayTask = null;
        if (context.ContextUser.UserType != UserType.User && trackSearch.Track.UserPlaycount > 0)
        {
            firstPlayTask = this._playService.GetTrackFirstPlayDate(context.ContextUser.UserId,
                trackSearch.Track.ArtistName, trackSearch.Track.TrackName);
        }

        Task<(int week, int month)> recentPlaycountsTask = null;
        if (trackSearch.Track.UserPlaycount.HasValue)
        {
            recentPlaycountsTask = this._playService.GetRecentTrackPlaycounts(context.ContextUser.UserId,
                trackSearch.Track.TrackName, trackSearch.Track.ArtistName);

            _ = this._updateService.CorrectUserTrackPlaycount(context.ContextUser.UserId,
                trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.Value);
        }

        Task<Guild> guildTask = null;
        Task<IDictionary<int, FullGuildUser>> guildUsersTask = null;
        if (context.DiscordGuild != null)
        {
            guildTask = this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
            guildUsersTask = this._guildService.GetGuildUsers(context.DiscordGuild.Id);
        }

        var dbTrack = await dbTrackTask;
        var userTitle = await userTitleTask;
        var featuredHistory = await featuredHistoryTask;
        var databaseAlbum = databaseAlbumTask != null ? await databaseAlbumTask : null;

        Task<EurovisionEntry> eurovisionTask = null;
        if (dbTrack?.SpotifyId != null)
        {
            eurovisionTask = this._eurovisionService.GetEurovisionEntryForSpotifyId(dbTrack.SpotifyId);
        }

        Task<TimeSpan> listeningTimeTask = null;
        var duration = dbTrack?.DurationMs ?? trackSearch.Track.Duration;
        if (duration is > 0 && trackSearch.Track.UserPlaycount > 1)
        {
            listeningTimeTask = this._timeService.GetPlayTimeForTrackWithPlaycount(trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.GetValueOrDefault());
        }

        Guild guild = null;
        IDictionary<int, FullGuildUser> guildUsers = null;
        Task<IList<WhoKnowsObjectWithUser>> indexedUsersTask = null;
        if (context.DiscordGuild != null)
        {
            guild = await guildTask;
            guildUsers = await guildUsersTask;

            if (guild?.LastIndexed != null)
            {
                indexedUsersTask = this._whoKnowsTrackService.GetIndexedUsersForTrack(context.DiscordGuild,
                    guildUsers, guild.GuildId, trackSearch.Track.ArtistName, trackSearch.Track.TrackName);
            }
        }

        string albumCoverUrl = null;
        var showThumbnail = false;
        if (databaseAlbum != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl ?? databaseAlbum.LastfmImageUrl;

            if (albumCoverUrl != null)
            {
                var safeForChannelTask = this._censorService.IsSafeForChannel(context.DiscordGuild,
                    context.DiscordChannel,
                    trackSearch.Track.AlbumName, trackSearch.Track.ArtistName, trackSearch.Track.AlbumUrl);
                var accentColorTask = this._albumService.GetAccentColorWithAlbum(context,
                    albumCoverUrl, databaseAlbum.Id, trackSearch.Track.AlbumName, trackSearch.Track.ArtistName);

                if (await safeForChannelTask == CensorService.CensorResult.Safe)
                {
                    showThumbnail = true;
                }

                response.ComponentsContainer.WithAccentColor(await accentColorTask);
            }
        }

        var headerSection = new StringBuilder();
        headerSection.AppendLine(trackSearch.Track.TrackUrl != null
            ? $"## [{trackSearch.Track.TrackName}]({trackSearch.Track.TrackUrl})"
            : $"## {trackSearch.Track.TrackName}");
        headerSection.AppendLine(trackSearch.Track.ArtistUrl != null
            ? $"Track by **[{trackSearch.Track.ArtistName}]({trackSearch.Track.ArtistUrl})**"
            : $"Track by **{trackSearch.Track.ArtistName}**");

        if (trackSearch.Track.AlbumName != null)
        {
            var albumType = databaseAlbum?.Type switch
            {
                "single" => "On single",
                "compilation" => "On compilation",
                _ => "On album"
            };

            var albumUrl = LastfmUrlExtensions.GetAlbumUrl(trackSearch.Track.ArtistName, trackSearch.Track.AlbumName);
            headerSection.Append(albumUrl != null
                ? $"-# {albumType} [{trackSearch.Track.AlbumName}]({albumUrl})"
                : $"-# {albumType} {trackSearch.Track.AlbumName}");
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

        if (trackSearch.Track.UserPlaycount.HasValue)
        {
            var userStats = new StringBuilder();

            var playsLine =
                $"**{trackSearch.Track.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(trackSearch.Track.UserPlaycount)} by **{userTitle}**";

            if (recentPlaycountsTask != null)
            {
                var recentPlaycounts = await recentPlaycountsTask;
                if (recentPlaycounts.month > 0)
                {
                    playsLine += $" — **{recentPlaycounts.month.Format(context.NumberFormat)}** last month";
                }
            }

            userStats.AppendLine(playsLine);

            if (listeningTimeTask != null)
            {
                var listeningTime = await listeningTimeTask;
                userStats.Append($"**{StringExtensions.GetLongListeningTimeString(listeningTime)}** listened");

                if (context.ContextUser.TotalPlaycount.HasValue && trackSearch.Track.UserPlaycount is >= 30)
                {
                    userStats.Append(
                        $" — **{((decimal)trackSearch.Track.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat)}** of all your plays");
                }

                userStats.AppendLine();
            }
            else if (context.ContextUser.TotalPlaycount.HasValue && trackSearch.Track.UserPlaycount is >= 30)
            {
                userStats.AppendLine(
                    $"**{((decimal)trackSearch.Track.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat)}** of all your plays");
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
                        $"*[Supporters]({Constants.GetSupporterOverviewLink}) can see track discovery dates.*");
                }
            }

            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(userStats.ToString().TrimEnd()));
        }

        var statsSection = new StringBuilder();

        if (context.DiscordGuild != null)
        {
            if (indexedUsersTask != null)
            {
                var usersWithTrack = await indexedUsersTask;
                var (_, filteredUsersWithTrack) =
                    WhoKnowsService.FilterWhoKnowsObjects(usersWithTrack, guildUsers, guild, context.ContextUser.UserId);

                if (filteredUsersWithTrack.Count != 0)
                {
                    var serverListeners = filteredUsersWithTrack.Count;
                    var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);

                    statsSection.AppendLine(
                        $"**{serverPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(serverPlaycount)} in this server by **{serverListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(serverListeners)}");
                }
            }

            var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
                guildUsers, guild, trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

            if (guildAlsoPlaying != null)
            {
                statsSection.AppendLine(guildAlsoPlaying);
            }
        }

        statsSection.AppendLine(
            $"**{trackSearch.Track.TotalPlaycount.Format(context.NumberFormat)}** Last.fm {StringExtensions.GetPlaysString(trackSearch.Track.TotalPlaycount)} by **{trackSearch.Track.TotalListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(trackSearch.Track.TotalListeners)}");

        var metaLine = new StringBuilder();
        if (dbTrack?.Popularity is > 0)
        {
            metaLine.Append($"**{dbTrack.Popularity}** popularity");
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

        if (trackSearch.IsRandom)
        {
            statsSection.AppendLine(
                $"Track #{trackSearch.RandomTrackPosition} ({trackSearch.RandomTrackPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(trackSearch.RandomTrackPlaycount)})");
        }

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(statsSection.ToString().TrimEnd()));

        var infoSection = new StringBuilder();

        var trackDuration = dbTrack?.DurationMs ?? trackSearch.Track.Duration;
        if (trackDuration is > 0)
        {
            infoSection.AppendLine($"`{StringExtensions.GetTrackLength(trackDuration.GetValueOrDefault())}` duration");
        }

        if (dbTrack != null && !string.IsNullOrEmpty(dbTrack.SpotifyId))
        {
            var pitch = StringExtensions.KeyIntToPitchString(dbTrack.Key.GetValueOrDefault());

            if (dbTrack.Tempo.HasValue)
            {
                infoSection.AppendLine($"`{pitch}` key — `{dbTrack.Tempo.Value:0.0}` bpm");
            }
            else
            {
                infoSection.AppendLine($"`{pitch}` key");
            }

            if (dbTrack.Danceability.HasValue && dbTrack.Energy.HasValue &&
                dbTrack.Instrumentalness.HasValue && dbTrack.Acousticness.HasValue &&
                dbTrack.Speechiness.HasValue && dbTrack.Liveness.HasValue && dbTrack.Valence.HasValue)
            {
                var danceability = ((decimal)dbTrack.Danceability).ToString("0%");
                var energetic = ((decimal)dbTrack.Energy).ToString("0%");
                var acoustic = ((decimal)dbTrack.Acousticness).ToString("0%");
                var instrumental = ((decimal)dbTrack.Instrumentalness).ToString("0%");
                var speechful = ((decimal)dbTrack.Speechiness).ToString("0%");
                var liveness = ((decimal)dbTrack.Liveness).ToString("0%");
                var valence = ((decimal)dbTrack.Valence).ToString("0%");

                infoSection.AppendLine($"`{danceability}` danceable — `{energetic}` energetic");
                infoSection.AppendLine($"`{acoustic}` acoustic — `{instrumental}` instrumental");
                infoSection.AppendLine($"`{speechful}` speechful — `{liveness}` liveness");
                infoSection.Append($"`{valence}` happy");
            }
        }

        if (infoSection.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(infoSection.ToString().TrimEnd()));
        }

        EurovisionEntry eurovisionEntry = null;
        if (eurovisionTask != null)
        {
            eurovisionEntry = await eurovisionTask;

            if (eurovisionEntry != null)
            {
                var eurovisionDescription = this._eurovisionService.GetEurovisionDescription(eurovisionEntry);
                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
                response.ComponentsContainer.AddComponent(
                    new TextDisplayProperties($"<:eurovision:1084971471610323035> Eurovision\n{eurovisionDescription.full}"));
            }
        }

        if (!string.IsNullOrWhiteSpace(trackSearch.Track.Description))
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(trackSearch.Track.Description));
        }

        var actionRow = new ActionRowProperties();

        if (!string.IsNullOrEmpty(dbTrack?.SpotifyId))
        {
            actionRow.WithButton(
                emote: EmojiProperties.Custom(DiscordConstants.Spotify),
                url: $"https://open.spotify.com/track/{dbTrack.SpotifyId}");
        }

        if (!string.IsNullOrEmpty(dbTrack?.AppleMusicUrl))
        {
            actionRow.WithButton(
                emote: EmojiProperties.Custom(DiscordConstants.AppleMusic),
                url: dbTrack.AppleMusicUrl);
        }

        if (eurovisionEntry?.VideoLink != null)
        {
            actionRow.WithButton(
                emote: EmojiProperties.Custom(DiscordConstants.YouTube), url: eurovisionEntry.VideoLink);
        }

        if (!string.IsNullOrEmpty(dbTrack?.SpotifyPreviewUrl) || !string.IsNullOrEmpty(dbTrack?.AppleMusicPreviewUrl))
        {
            actionRow.WithButton(
                "Preview",
                $"{InteractionConstants.TrackPreview}:{dbTrack.Id}:",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Custom(DiscordConstants.PlayPreview));
        }

        if (SupporterService.IsSupporter(context.ContextUser.UserType) &&
            !string.IsNullOrWhiteSpace(dbTrack?.PlainLyrics))
        {
            actionRow.WithButton(
                "Lyrics",
                $"{InteractionConstants.TrackLyrics}:{dbTrack.Id}:",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Standard("🎤"));
        }

        response.ComponentsContainer.WithActionRow(actionRow);

        return response;
    }

    public async Task<ResponseModel> WhoKnowsTrackAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string trackValues,
        bool displayRoleSelector = false,
        List<ulong> roles = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var track = await this._trackService.SearchTrack(response, context.DiscordUser, trackValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedTracks: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (track.Track == null)
        {
            return track.Response;
        }

        var cachedTrack = await this._musicDataFactory.GetOrStoreTrackAsync(track.Track);

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithTrack = await this._whoKnowsTrackService.GetIndexedUsersForTrack(context.DiscordGuild, guildUsers,
            guild.GuildId, track.Track.ArtistName, track.Track.TrackName);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.DiscordUser.Id);
        var currentUser =
            await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers,
            await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId), currentUser.UserId, guild);

        usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, context.ContextUser,
            trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var (filterStats, filteredUsersWithTrack) =
            WhoKnowsService.FilterWhoKnowsObjects(usersWithTrack, guildUsers, guild, context.ContextUser.UserId, roles);

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track",
                $"in <b>{context.DiscordGuild.Name}</b>", albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = StringExtensions.TruncateLongString($"{trackName} in {context.DiscordGuild.Name}", 255);

        var footer = new StringBuilder();

        if (track.IsRandom)
        {
            footer.AppendLine(
                $"Track #{track.RandomTrackPosition} ({track.RandomTrackPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(track.RandomTrackPlaycount)})");
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

        if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
        {
            var serverListeners = filteredUsersWithTrack.Count;
            var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

            footer.Append($"Track - ");
            footer.Append(
                $"{serverListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append(
                $"{serverPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.AppendLine($"{((int)avgServerPlaycount).Format(context.NumberFormat)} avg");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
            guildUsers, guild, track.Track.ArtistName, track.Track.TrackName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithTrack,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString());

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, context.ContextUser.UserId,
                PrivacyLevel.Server, context.NumberFormat);
        if (filteredUsersWithTrack.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this track.";
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (track.Track.TrackUrl != null)
        {
            response.Embed.WithUrl(track.Track.TrackUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (displayRoleSelector)
        {
            if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
            {
                var allowedRoles = new RoleMenuProperties($"{InteractionConstants.WhoKnowsTrackRolePicker}:{cachedTrack.Id}")
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

    public async Task<ResponseModel> FriendsWhoKnowTrackAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string trackValues)
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

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild?.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        var track = await this._trackService.SearchTrack(response, context.DiscordUser, trackValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedTracks: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (track.Track == null)
        {
            return track.Response;
        }

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var usersWithTrack = await this._whoKnowsTrackService.GetFriendUsersForTrack(context.DiscordGuild, guildUsers,
            guild?.GuildId ?? 0, context.ContextUser.UserId, track.Track.ArtistName, track.Track.TrackName);

        usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, context.ContextUser,
            trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"from <b>{userTitle}</b>'s friends",
                albumCoverUrl, trackName,
                usersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"friends-whoknow-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = $"{trackName} with friends";

        var footer = "";

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer +=
                $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
        }

        if (usersWithTrack.Any() && usersWithTrack.Count() > 1)
        {
            var globalListeners = usersWithTrack.Count();
            var globalPlaycount = usersWithTrack.Sum(a => a.Playcount);
            var avgPlaycount = usersWithTrack.Average(a => a.Playcount);

            footer +=
                $"\n{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ";
            footer +=
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ";
            footer += $"{((int)avgPlaycount).Format(context.NumberFormat)} avg";
        }

        footer += $"\nFriends WhoKnow track for {userTitle}";

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(usersWithTrack,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(usersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server,
                context.NumberFormat);
        if (!usersWithTrack.Any())
        {
            serverUsers = "None of your friends have listened to this track.";
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (Uri.IsWellFormedUriString(track.Track.TrackUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(track.Track.TrackUrl);
        }

        response.EmbedFooter.WithText(footer);
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> GlobalWhoKnowsTrackAsync(
        ContextModel context,
        WhoKnowsSettings settings,
        string trackValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var track = await this._trackService.SearchTrack(response, context.DiscordUser, trackValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedTracks: true,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (track.Track == null)
        {
            return track.Response;
        }

        var spotifyTrack = await this._musicDataFactory.GetOrStoreTrackAsync(track.Track);

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var usersWithTrack = await this._whoKnowsTrackService.GetGlobalUsersForTrack(context.DiscordGuild,
            track.Track.ArtistName, track.Track.TrackName);

        var filteredUsersWithTrack =
            await this._whoKnowsService.FilterGlobalUsersAsync(usersWithTrack, settings.QualityFilterDisabled);

        filteredUsersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(filteredUsersWithTrack,
            context.ContextUser, trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var privacyLevel = PrivacyLevel.Global;

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        var footer = new StringBuilder();

        footer = WhoKnowsService.GetGlobalWhoKnowsFooter(footer, settings, context);

        if (context.DiscordGuild != null)
        {
            var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);
            var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            filteredUsersWithTrack =
                WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithTrack, guildUsers);

            if (settings.AdminView)
            {
                privacyLevel = PrivacyLevel.Server;
            }

            var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
                guildUsers, guild, track.Track.ArtistName, track.Track.TrackName);

            if (guildAlsoPlaying != null)
            {
                footer.AppendLine(guildAlsoPlaying);
            }
        }

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"in <b>.fmbot 🌐</b>",
                albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"global-whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var title = StringExtensions.TruncateLongString($"{trackName} globally", 255);

        if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
        {
            var globalListeners = filteredUsersWithTrack.Count;
            var globalPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
            var avgPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

            footer.Append($"Global track - ");
            footer.Append(
                $"{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append(
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{((int)avgPlaycount).Format(context.NumberFormat)} avg");
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithTrack,
                context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                title, footer.ToString(), hidePrivateUsers: settings.HidePrivateUsers);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, context.ContextUser.UserId,
            privacyLevel, context.NumberFormat, hidePrivateUsers: settings.HidePrivateUsers);
        if (!filteredUsersWithTrack.Any())
        {
            serverUsers = "Nobody that uses .fmbot has listened to this track.";
        }

        response.Embed.WithDescription(serverUsers);

        var duration = spotifyTrack?.DurationMs ?? track.Track.Duration;

        if (duration is > 0)
        {
            var trackLength = TimeSpan.FromMilliseconds(duration.GetValueOrDefault());
            if (trackLength < TimeSpan.FromSeconds(60) &&
                track.Track.UserPlaycount > 2500)
            {
                response.Embed.AddField("Heads up",
                    "We regularly remove people who spam short songs to raise their playcounts from Global WhoKnows. " +
                    "Consider not spamming scrobbles and/or removing your scrobbles on this track if you don't want to be removed.");

                Log.Information(
                    "Displayed GlobalWhoKnows short track warning for {userId} - {discordUserId} - {userNameLastFm} - {artistName} | {trackName}",
                    context.ContextUser.UserId, context.ContextUser.DiscordUserId, context.ContextUser.UserNameLastFM,
                    track.Track.ArtistName, track.Track.TrackName);
            }
        }

        response.Embed.WithTitle(title);

        if (!string.IsNullOrWhiteSpace(track.Track.TrackUrl))
        {
            response.Embed.WithUrl(track.Track.TrackUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    private async Task<string> GetAlbumCoverUrl(ContextModel context, TrackSearch track, ResponseModel response)
    {
        var databaseAlbum =
            await this._albumService.GetAlbumFromDatabase(track.Track.ArtistName, track.Track.AlbumName);

        var albumCoverUrl = databaseAlbum?.LastfmImageUrl;

        if (databaseAlbum?.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        if (albumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                track.Track.AlbumName, track.Track.ArtistName, track.Track.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnail(albumCoverUrl);
            }
            else
            {
                albumCoverUrl = null;
            }

            var accentColor = await this._albumService.GetAccentColorWithAlbum(context,
                albumCoverUrl, databaseAlbum?.Id, track.Track.AlbumName, track.Track.ArtistName);

            response.Embed.WithColor(accentColor);
        }

        return albumCoverUrl;
    }

    public async Task<ResponseModel> TrackPlays(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            otherUserUsername: userSettings.UserNameLastFm, userId: context.ContextUser.UserId,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has **{trackSearch.Track.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(trackSearch.Track.UserPlaycount)} " +
            $"for **{StringExtensions.Sanitize(trackSearch.Track.TrackName)}** by **{StringExtensions.Sanitize(trackSearch.Track.ArtistName)}**";

        if (trackSearch.Track.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserTrackPlaycount(context.ContextUser.UserId,
                trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.Value);
        }

        if (userSettings.DifferentUser)
        {
            await this._updateService.UpdateUser(new UpdateUserQueueItem(userSettings.UserId));
        }

        var recentTrackPlaycounts =
            await this._playService.GetRecentTrackPlaycounts(userSettings.UserId, trackSearch.Track.TrackName,
                trackSearch.Track.ArtistName);
        if (recentTrackPlaycounts.month != 0)
        {
            reply +=
                $"\n-# *{recentTrackPlaycounts.week.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentTrackPlaycounts.week)} last week — " +
                $"{recentTrackPlaycounts.month.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentTrackPlaycounts.month)} last month*";
        }

        response.Text = reply;

        return response;
    }

    public async Task<ResponseModel> TrackDetails(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var spotifyTrack = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track);

        var reply = new StringBuilder();

        reply.Append(
            $"**{StringExtensions.Sanitize(trackSearch.Track.TrackName)}** by **{StringExtensions.Sanitize(trackSearch.Track.ArtistName)}**");

        var duration = spotifyTrack?.DurationMs ?? trackSearch.Track.Duration;

        var formattedTrackLength = StringExtensions.GetTrackLength(duration.GetValueOrDefault());

        if (spotifyTrack is { Tempo: not null } && duration.HasValue)
        {
            var bpm = $"{spotifyTrack.Tempo.Value:0.0}";
            var pitch = StringExtensions.KeyIntToPitchString(spotifyTrack.Key.GetValueOrDefault());

            reply.Append($" has `{bpm}` bpm, is in key `{pitch}` and lasts `{formattedTrackLength}`");
        }
        else
        {
            if (trackSearch.Track.Duration.HasValue && trackSearch.Track.Duration != 0)
            {
                reply.Append($" lasts `{formattedTrackLength}` (No Spotify track metadata found)");
            }
            else
            {
                reply.Append(
                    $" is a track that we don't have any metadata for, sorry <:Whiskeydogearnest:1097591075822129292>");
            }
        }

        response.Text = reply.ToString();

        if (!string.IsNullOrEmpty(spotifyTrack?.SpotifyPreviewUrl) || !string.IsNullOrEmpty(spotifyTrack?.AppleMusicPreviewUrl))
        {
            response.Components = new ActionRowProperties()
                .WithButton(
                    "Preview",
                    $"{InteractionConstants.TrackPreview}:{spotifyTrack.Id}:",
                    style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PlayPreview));
        }

        return response;
    }

    public async Task<ResponseModel> TrackPreviewAsync(ContextModel context,
        string searchValue,
        string interactionToken)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var track = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track);

        var previewUrl = track.SpotifyPreviewUrl ?? track.AppleMusicPreviewUrl;

        try
        {
            await this._discordSkuService.SendVoiceMessage(previewUrl, interactionToken);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while sending voice message followup");
            throw;
        }

        return response;
    }

    public async Task<ResponseModel> LoveTrackAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var trackDescription = LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track);

        var container = response.ComponentsContainer;
        container.WithAccentColor(await UserService.GetAccentColor(context.ContextUser, context.DiscordGuild));

        if (trackSearch.Track.Loved)
        {
            container.WithTextDisplay($"### ❤️ Track already loved");
            container.WithTextDisplay(trackDescription);
        }
        else
        {
            var trackLoved = await this._dataSourceFactory.LoveTrackAsync(context.ContextUser.SessionKeyLastFm,
                trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

            if (trackLoved)
            {
                container.WithTextDisplay($"### ❤️ Loved track for {userTitle}");
                container.WithTextDisplay(trackDescription);
            }
            else
            {
                response.Text = "Something went wrong while adding loved track.";
                response.ResponseType = ResponseType.Text;
                response.CommandResponse = CommandResponse.Error;
                return response;
            }
        }

        return response;
    }

    public async Task<ResponseModel> UnLoveTrackAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var trackDescription = LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track);

        var container = response.ComponentsContainer;
        container.WithAccentColor(await UserService.GetAccentColor(context.ContextUser, context.DiscordGuild));

        if (!trackSearch.Track.Loved)
        {
            container.WithTextDisplay($"### 💔 Track wasn't loved");
            container.WithTextDisplay(trackDescription);
        }
        else
        {
            var trackLoved = await this._dataSourceFactory.UnLoveTrackAsync(context.ContextUser.SessionKeyLastFm,
                trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

            if (trackLoved)
            {
                container.WithTextDisplay($"### 💔 Unloved track for {userTitle}");
                container.WithTextDisplay(trackDescription);
            }
            else
            {
                response.Text = "Something went wrong while unloving track.";
                response.ResponseType = ResponseType.Text;
                response.CommandResponse = CommandResponse.Error;
                return response;
            }
        }

        return response;
    }

    public async Task<ResponseModel> LovedTracksAsync(
        ContextModel context,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var pages = new List<PageBuilder>();
        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
        {
            sessionKey = context.ContextUser.SessionKeyLastFm;
        }

        const int amount = 200;

        var lovedTracks =
            await this._dataSourceFactory.GetLovedTracksAsync(userSettings.UserNameLastFm, amount,
                sessionKey: sessionKey);

        if (!lovedTracks.Content.RecentTracks.Any())
        {
            response.Embed.WithDescription(
                $"The Last.fm user `{userSettings.UserNameLastFm}` has no loved tracks yet! \n" +
                $"Use `{context.Prefix}love` to add tracks to your list.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(lovedTracks))
        {
            var errorResponse =
                GenericEmbedService.RecentScrobbleCallFailedResponse(lovedTracks, userSettings.UserNameLastFm);
            return errorResponse;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var title = !userSettings.DifferentUser
            ? userTitle
            : $"{userSettings.UserNameLastFm}, requested by {userTitle}";

        response.EmbedAuthor.WithName($"Last loved tracks for {title}");
        response.EmbedAuthor.WithUrl(lovedTracks.Content.UserRecentTracksUrl);

        if (!userSettings.DifferentUser)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl()?.ToString());
        }

        var firstTrack = lovedTracks.Content.RecentTracks[0];

        var footer =
            $"{userSettings.UserNameLastFm} has {lovedTracks.Content.TotalAmount.Format(context.NumberFormat)} loved tracks";
        DateTime? timePlaying = null;

        if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
        {
            timePlaying = firstTrack.TimePlayed.Value;
        }

        if (timePlaying.HasValue)
        {
            footer += " | Last loved track:";
        }

        var lovedTrackPages = lovedTracks.Content.RecentTracks.ChunkBy(10);

        var counter = lovedTracks.Content.TotalAmount;
        foreach (var lovedTrackPage in lovedTrackPages)
        {
            var lovedPageString = new StringBuilder();
            foreach (var lovedTrack in lovedTrackPage)
            {
                var trackString = LastFmRepository.TrackToOneLinedLinkedString(lovedTrack);

                lovedPageString.AppendLine($"`{counter}` - {trackString}");
                counter--;
            }

            var page = new PageBuilder()
                .WithDescription(lovedPageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer);

            if (timePlaying.HasValue)
            {
                page.WithTimestamp(timePlaying);
            }

            pages.Add(page);
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> GuildTracksAsync(
        ContextModel context,
        Guild guild,
        GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        ICollection<GuildTrack> topGuildTracks;
        List<GuildTrack> previousTopGuildTracks = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildTracks = await this._whoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId,
                guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            topGuildTracks = await this._playService.GetGuildTopTracksPlays(guild.GuildId,
                guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue, guildListSettings.EndDateTime);
            previousTopGuildTracks = (await this._playService.GetGuildTopTracksPlays(guild.GuildId,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue, guildListSettings.BillboardEndDateTime)).ToList();
        }

        if (topGuildTracks.Count == 0)
        {
            response.Embed.WithDescription(guildListSettings.NewSearchValue != null
                ? $"Sorry, there are no registered top tracks for artist `{guildListSettings.NewSearchValue}` on this server in the time period you selected."
                : $"Sorry, there are no registered top tracks on this server in the time period you selected.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue)
            ? $"Top {guildListSettings.TimeDescription.ToLower()} tracks in {context.DiscordGuild.Name}"
            : $"Top {guildListSettings.TimeDescription.ToLower()} '{guildListSettings.NewSearchValue}' tracks in {context.DiscordGuild.Name}";

        var footerLabel = guildListSettings.OrderType == OrderType.Listeners
            ? "Listener count"
            : "Play count";

        string footerHint = new Random().Next(0, 5) switch
        {
            1 => $"View specific track listeners with '{context.Prefix}whoknowstrack'",
            2 => "Available time periods: alltime, monthly, weekly, current and last month",
            3 => "Available sorting options: plays and listeners",
            _ => null
        };

        var trackPages = topGuildTracks.Chunk(12).ToList();

        var counter = 1;
        var pageDescriptions = new List<string>();
        foreach (var page in trackPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in page)
            {
                var trackName = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue)
                    ? $"**{track.ArtistName}** - **{track.TrackName}**"
                    : $"**{track.TrackName}**";
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{track.ListenerCount.Format(context.NumberFormat)}` · {trackName} · *{track.TotalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(track.TotalPlaycount)}*"
                    : $"`{track.TotalPlaycount.Format(context.NumberFormat)}` · {trackName} · *{track.ListenerCount.Format(context.NumberFormat)} {StringExtensions.GetListenersString(track.ListenerCount)}*";

                if (previousTopGuildTracks != null && previousTopGuildTracks.Any())
                {
                    var previousTopTrack = previousTopGuildTracks.FirstOrDefault(f =>
                        f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                    int? previousPosition = previousTopTrack == null
                        ? null
                        : previousTopGuildTracks.IndexOf(previousTopTrack);

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

    public async Task<ResponseModel> TopTracksAsync(ContextModel context,
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

        var userUrl =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/tracks?{timeSettings.UrlParameter}";
        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var topTracks = await this._dataSourceFactory.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, topListSettings.ListAmount,
            calculateTimeListened: topListSettings.Type == TopListType.TimeListened);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (mode == ResponseMode.Image)
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            var backgroundImage = (await this._artistsService.GetArtistFromDatabase(topTracks.Content.TopTracks.First()
                .ArtistName))?.SpotifyImageUrl;

            using var image = await this._puppeteerService.GetTopList(userTitle, "Top Tracks", "tracks",
                timeSettings.Description,
                topTracks.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), backgroundImage,
                topTracks.TopList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-tracks-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var previousTopTracks = new List<TopTrack>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousTopTracksCall = await this._dataSourceFactory
                .GetTopTracksForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200,
                    calculateTimeListened: topListSettings.Type == TopListType.TimeListened);

            if (previousTopTracksCall.Success)
            {
                previousTopTracks.AddRange(previousTopTracksCall.Content.TopTracks);
            }
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        if (topListSettings.Type == TopListType.TimeListened)
        {
            topTracks.Content.TopTracks = topTracks.Content.TopTracks
                .OrderByDescending(o => o.TimeListened.TotalTimeListened)
                .ToList();

            previousTopTracks = previousTopTracks
                .OrderByDescending(o => o.TimeListened.TotalTimeListened)
                .ToList();
        }

        var trackPages = topTracks.Content.TopTracks.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var trackPage in trackPages)
        {
            var tooMuchChars = trackPage.Select(s => s.TrackUrl?.Length).Sum() > 3000;

            var trackPageString = new StringBuilder();
            foreach (var track in trackPage)
            {
                var name = new StringBuilder();
                if (!tooMuchChars)
                {
                    name.Append(
                        $"**{StringExtensions.Sanitize(track.ArtistName)}** - **[{track.TrackName}]({track.TrackUrl})** ");
                }
                else
                {
                    name.Append($"**{StringExtensions.Sanitize(track.ArtistName)}** - **{track.TrackName}** ");
                }

                if (topListSettings.Type == TopListType.Plays)
                {
                    name.Append(
                        $"- *{track.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(track.UserPlaycount)}*");
                }
                else
                {
                    name.Append(
                        $"- *{StringExtensions.GetListeningTimeString(track.TimeListened.TotalTimeListened)}*");
                }

                if (topListSettings.Billboard && previousTopTracks.Any())
                {
                    var previousTopTrack = previousTopTracks.FirstOrDefault(f =>
                        f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                    int? previousPosition =
                        previousTopTrack == null ? null : previousTopTracks.IndexOf(previousTopTrack);

                    trackPageString.AppendLine(StringService
                        .GetBillboardLine(name.ToString(), counter - 1, previousPosition).Text);
                }
                else
                {
                    trackPageString.Append($"{counter}. ");
                    trackPageString.AppendLine(name.ToString());
                }

                counter++;
            }

            var footer = new StringBuilder();

            ImportService.AddImportDescription(footer, topTracks.PlaySources);

            footer.Append($"Page {pageCounter}/{trackPages.Count}");
            if (topTracks.Content.TotalAmount.HasValue)
            {
                footer.Append(
                    $" - {topTracks.Content.TotalAmount.Value.Format(context.NumberFormat)} different tracks");
            }

            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine();
                footer.Append("View as billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(trackPageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> GetReceipt(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (userSettings.DifferentUser)
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/tracks?{timeSettings.UrlParameter}";

        var topTracks = await this._dataSourceFactory.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 20);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
            timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

        using var image = await this._puppeteerService.GetReceipt(userSettings, topTracks.Content, timeSettings, count);

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.FileName = "receipt.png";

        var embedTitle = $"[Top {timeSettings.Description} tracks]({userUrl}) for {userSettings.DisplayName}";

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"));

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));
        response.ComponentsContainer.AddComponent(new MediaGalleryProperties { mediaGallery });
        response.ResponseType = ResponseType.ComponentsV2;

        return response;
    }

    public async Task<ResponseModel> ScrobbleAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (string.IsNullOrWhiteSpace(searchValue))
        {
            response.ResponseType = ResponseType.ComponentsV2;
            response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

            response.ComponentsContainer.WithTextDisplay(
                $"### {context.Prefix}scrobble\n" +
                $"Scrobbles a track on Last.fm. You can search for a track, enter the exact name with separators, scrobble from a Discogs link, or scrobble along with another user.");
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay(
                $"`{context.Prefix}sb searchquery`\n" +
                $"`{context.Prefix}scrobble Artist | Track`\n" +
                $"`{context.Prefix}scrobble Artist | Track | Album`\n" +
                $"`{context.Prefix}scrobble @user` · `/scrobble lfm:username`\n" +
                $"`{context.Prefix}scrobble https://www.discogs.com/release/...`");

            response.CommandResponse = CommandResponse.Help;
            return response;
        }

        var commandExecutedCount = await this._userService.GetCommandExecutedAmount(context.ContextUser.UserId,
            "scrobble", DateTime.UtcNow.AddMinutes(-30));
        var maxCount = SupporterService.IsSupporter(context.ContextUser.UserType) ? 25 : 12;

        if (commandExecutedCount > maxCount)
        {
            var reply = new StringBuilder();
            reply.AppendLine("Please wait before scrobbling to Last.fm again.");

            var globalWhoKnowsCount = await this._userService.GetCommandExecutedAmount(context.ContextUser.UserId,
                "globalwhoknows", DateTime.UtcNow.AddHours(-3));
            if (globalWhoKnowsCount >= 1)
            {
                reply.AppendLine();
                reply.AppendLine(
                    "Note that users who add fake scrobbles or scrobble from multiple sources at the same time might be subject to removal from Global WhoKnows.");
            }

            response.Embed.WithDescription(reply.ToString());
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (searchValue.Contains("discogs.com", StringComparison.OrdinalIgnoreCase))
        {
            return await ScrobbleDiscogsAsync(context, response, searchValue, userTitle);
        }

        var track = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (track.Track == null)
        {
            return track.Response;
        }

        if (searchValue.Contains(" | ") && searchValue.Split(" | ").ElementAtOrDefault(2) != null)
        {
            track.Track.AlbumName = searchValue.Split(" | ").ElementAt(2);
        }

        var trackScrobbled = await this._dataSourceFactory.ScrobbleAsync(context.ContextUser.SessionKeyLastFm,
            track.Track.ArtistName, track.Track.TrackName, track.Track.AlbumName);

        if (trackScrobbled.Success && trackScrobbled.Content.Accepted)
        {
            Statistics.LastfmScrobbles.Inc();
            response.Embed.WithTitle($"Scrobbled track for {userTitle}");
            response.Embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track.Track));
        }
        else if (trackScrobbled.Success && trackScrobbled.Content.Ignored)
        {
            response.Embed.WithTitle($"Last.fm ignored scrobble for {userTitle}");
            var description = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(trackScrobbled.Content.IgnoreMessage))
            {
                description.AppendLine($"Reason: {trackScrobbled.Content.IgnoreMessage}");
            }

            description.AppendLine(LastFmRepository.ResponseTrackToLinkedString(track.Track));
            response.Embed.WithDescription(description.ToString());
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
        }
        else
        {
            response.Embed.WithDescription("Something went wrong while scrobbling track.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    private async Task<ResponseModel> ScrobbleDiscogsAsync(
        ContextModel context,
        ResponseModel response,
        string searchValue,
        string userTitle)
    {
        if (context.ContextUser.UserDiscogs?.AccessToken == null)
        {
            response.Embed.WithDescription(
                "To use the Discogs commands you have to connect a Discogs account.\n\n" +
                $"Use the `{context.Prefix}discogs` command to get started.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.UsernameNotSet;
            return response;
        }

        var releaseId = DiscogsService.DiscogsReleaseUrlToId(searchValue);
        if (!releaseId.HasValue)
        {
            response.Embed.WithDescription("Invalid Discogs release url.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var release = await this._discogsService.GetDiscogsRelease(context.ContextUser.UserId, releaseId.Value);

        if (release == null)
        {
            response.Embed.WithDescription(
                "Could not fetch release from Discogs. Please try again and check your URL.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var reply = new StringBuilder();
        var scrobbleTime = DateTime.UtcNow;
        var artistName = Regex.Replace(release.Artists.First().Name, @"\(\d+\)", "").TrimEnd();

        foreach (var track in release.Tracklist)
        {
            TimeSpan? trackLength = null;

            if (!string.IsNullOrWhiteSpace(track.Duration))
            {
                var splitDuration = track.Duration.Split(":");
                if (int.TryParse(splitDuration[0], out var minutes) &&
                    int.TryParse(splitDuration[1], out var seconds))
                {
                    var totalSeconds = minutes * 60 + seconds;
                    trackLength = TimeSpan.FromSeconds(totalSeconds);
                }
            }
            else
            {
                var length = await this._timeService.GetTrackLengthForTrack(artistName, track.Title);
                if (length.TotalSeconds != 0)
                {
                    trackLength = length;
                }
            }

            var trackScrobbled = await this._dataSourceFactory.ScrobbleAsync(context.ContextUser.SessionKeyLastFm,
                artistName, track.Title, release.Title, scrobbleTime);

            reply.Append($"- ");
            if (trackScrobbled.Success)
            {
                var dateValue = ((DateTimeOffset)scrobbleTime).ToUnixTimeSeconds();
                reply.Append($"<t:{dateValue}:t>");
            }
            else
            {
                reply.Append("Last.fm error");
            }

            reply.Append($" - `{track.Position}` - **{track.Title}**");

            if (trackLength.HasValue)
            {
                var formattedTrackLength =
                    $"{(trackLength.Value.Hours == 0 ? "" : $"{trackLength.Value.Hours}:")}{trackLength.Value.Minutes}:{trackLength.Value.Seconds:D2}";
                reply.Append($" - `{formattedTrackLength}`");
            }

            reply.AppendLine();

            if (trackLength.HasValue)
            {
                scrobbleTime = scrobbleTime.Add(trackLength.Value);
            }
        }

        response.Embed.WithTitle($"Scrobbling Discogs: {artistName} - {release.Title}");
        response.Embed.WithUrl(release.Uri);
        response.Embed.WithDescription(reply.ToString());
        response.Embed.WithFooter($"Scrobbled for {userTitle} - Scrobbling into the future\n" +
                                  $"Use this command when you start listening to ensure accurate timestamps");

        return response;
    }

    public async Task<ResponseModel> TrackLyricsAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);

        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var title = $"### Lyrics for [{StringExtensions.Sanitize(trackSearch.Track.ArtistName)} - {StringExtensions.Sanitize(trackSearch.Track.TrackName)}]({trackSearch.Track.TrackUrl})";

        var track = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track, true);

        if (track == null || track.PlainLyrics == null)
        {
            response.ResponseType = ResponseType.ComponentsV2;
            response.ComponentsContainer.WithTextDisplay(title);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay("Sorry, we don't have the lyrics for this track.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var allLines = track.PlainLyrics.Split('\n').ToList();
        var lyricsPages = new List<LyricPage>();
        const int linesPerPage = 20;

        var syncedLyricIndex = 0;
        var hasSyncedLyrics = track.SyncedLyrics != null && track.SyncedLyrics.Any();

        for (var i = 0; i < allLines.Count; i += linesPerPage)
        {
            var pageLines = allLines.Skip(i).Take(linesPerPage).ToList();

            TimeSpan? start = null;
            TimeSpan? end = null;
            var segments = new List<string>();
            var currentSegment = new StringBuilder();
            TimeSpan? previousTimestamp = null;

            if (hasSyncedLyrics)
            {
                var firstLine = track.SyncedLyrics.ElementAtOrDefault(syncedLyricIndex);
                if (firstLine != null)
                {
                    start = firstLine.Timestamp;
                }

                foreach (var line in pageLines)
                {
                    if (line == "")
                    {
                        currentSegment.AppendLine();
                        continue;
                    }

                    var syncedLine = track.SyncedLyrics.ElementAtOrDefault(syncedLyricIndex);
                    if (syncedLine == null)
                    {
                        currentSegment.AppendLine(line);
                        continue;
                    }

                    var closeness = GameService.GetLevenshteinDistance(syncedLine.Text, line);
                    TimeSpan currentTimestamp;
                    if (closeness > 2)
                    {
                        syncedLyricIndex++;
                        syncedLine = track.SyncedLyrics.ElementAtOrDefault(syncedLyricIndex);
                        if (syncedLine == null)
                        {
                            currentSegment.AppendLine(line);
                            continue;
                        }

                        currentTimestamp = syncedLine.Timestamp;
                        end = currentTimestamp;
                    }
                    else
                    {
                        currentTimestamp = syncedLine.Timestamp;
                        end = currentTimestamp;
                        syncedLyricIndex++;
                    }

                    if (previousTimestamp.HasValue &&
                        (currentTimestamp - previousTimestamp.Value).TotalSeconds > 30)
                    {
                        segments.Add(currentSegment.ToString().TrimEnd());
                        currentSegment.Clear();
                    }

                    currentSegment.AppendLine(line);
                    previousTimestamp = currentTimestamp;
                }
            }
            else
            {
                foreach (var line in pageLines)
                {
                    currentSegment.AppendLine(line);
                }
            }

            if (currentSegment.Length > 0)
            {
                segments.Add(currentSegment.ToString().TrimEnd());
            }

            lyricsPages.Add(new LyricPage(segments, start, end));
        }

        if (lyricsPages.Count == 1)
        {
            response.ResponseType = ResponseType.ComponentsV2;
            response.ComponentsContainer.WithTextDisplay(title);
            response.ComponentsContainer.WithSeparator();
            AddLyricSegments(response.ComponentsContainer, lyricsPages[0].Segments);
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;

            var paginator = new ComponentPaginatorBuilder()
                .WithPageFactory(GeneratePage)
                .WithPageCount(lyricsPages.Count)
                .WithActionOnTimeout(ActionOnStop.DisableInput);

            response.ComponentPaginator = paginator;

            IPage GeneratePage(IComponentPaginator p)
            {
                var container = new ComponentContainerProperties();
                var lyricPage = lyricsPages[p.CurrentPageIndex];

                container.WithTextDisplay(title);
                container.WithSeparator();
                AddLyricSegments(container, lyricPage.Segments);
                container.WithSeparator();

                var timeLabel = "";
                if (lyricPage.Start.HasValue && lyricPage.End.HasValue)
                {
                    timeLabel =
                        $"{StringExtensions.GetTrackLength(lyricPage.Start.Value)} until {StringExtensions.GetTrackLength(lyricPage.End.Value.Add(TimeSpan.FromSeconds(3)))}";
                }
                else if (track.DurationMs.HasValue)
                {
                    timeLabel = StringExtensions.GetTrackLength(track.DurationMs.Value);
                }

                var navRow = new ActionRowProperties()
                    .AddPreviousButton(p, style: ButtonStyle.Secondary,
                        emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious));

                if (!string.IsNullOrEmpty(timeLabel))
                {
                    navRow.WithButton(label: timeLabel, customId: "lyrics-time", style: ButtonStyle.Secondary,
                        disabled: true);
                }

                navRow.AddNextButton(p, style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PagesNext));

                container.WithActionRow(navRow);

                return new PageBuilder()
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithMessageFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([container])
                    .Build();
            }
        }

        response.CommandResponse = CommandResponse.Ok;
        return response;

        static void AddLyricSegments(ComponentContainerProperties container, List<string> segments)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                {
                    container.WithSeparator();
                }

                container.WithTextDisplay(segments[i]);
            }
        }
    }

    private record LyricPage(List<string> Segments, TimeSpan? Start, TimeSpan? End);

    public static ResponseModel LyricsSupporterRequired(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (!SupporterService.IsSupporter(context.ContextUser.UserType))
        {
            response.Embed.WithDescription(
                "Viewing track lyrics in .fmbot is only available for .fmbot supporters.");

            response.Components = new ActionRowProperties()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "lyrics"));
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public Task<ResponseModel> ScrobbleFromUserAsync(
        ContextModel context,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var displayName = StringExtensions.Sanitize(userSettings.DisplayName);

        var button = new ButtonProperties($"{InteractionConstants.ScrobbleFromUser}:{userSettings.UserId}",
            "View recent scrobbles", ButtonStyle.Primary);

        var section = new ComponentSectionProperties(button,
            [new TextDisplayProperties(
                $"Listening along with **{displayName}** and not scrobbling yourself? View their recent scrobbles and add them to your own history.")]);

        response.ComponentsContainer.AddComponent(section);

        return Task.FromResult(response);
    }

    public async Task<ResponseModel> ScrobbleFromUserPaginatorAsync(
        ContextModel context,
        int targetUserId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var targetUser = await this._userService.GetUserForIdAsync(targetUserId);
        if (targetUser == null)
        {
            response.ResponseType = ResponseType.ComponentsV2;
            response.ComponentsContainer.WithTextDisplay("Could not find the target user.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        await Task.WhenAll(
            this._updateService.UpdateUser(targetUser),
            this._updateService.UpdateUser(context.ContextUser));

        var targetDisplayName = targetUser.UserNameLastFM;

        var recentTracks = await this._playService.GetCachedPlaysForUser(targetUserId);

        if (recentTracks.Count == 0)
        {
            response.ResponseType = ResponseType.ComponentsV2;
            response.ComponentsContainer.WithTextDisplay($"No cached plays found for {targetDisplayName}. They might need to run a command first to index their plays.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var userPlaysWithTimestamps = recentTracks
            .Where(t => t.TimePlayed.HasValue)
            .ToList();

        var alreadyScrobbledTimestamps = new HashSet<DateTime>();
        if (userPlaysWithTimestamps.Count > 0)
        {
            var contextUserPlays = await this._playService.GetCachedPlaysForUser(context.ContextUser.UserId,
                limit: 9999);

            foreach (var targetPlay in userPlaysWithTimestamps)
            {
                if (contextUserPlays.Any(p =>
                        p.TimePlayed.HasValue &&
                        Math.Abs((p.TimePlayed.Value - targetPlay.TimePlayed!.Value).TotalSeconds) <= 30 &&
                        string.Equals(p.ArtistName, targetPlay.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.TrackName, targetPlay.TrackName, StringComparison.OrdinalIgnoreCase)))
                {
                    alreadyScrobbledTimestamps.Add(targetPlay.TimePlayed!.Value);
                }
            }
        }

        var trackPages = recentTracks
            .ChunkBy(6)
            .ToList();

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(trackPages.Count)
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;

        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var pageIndex = p.CurrentPageIndex;
            var trackPage = trackPages.ElementAtOrDefault(pageIndex);

            var container = new ComponentContainerProperties();

            container.WithTextDisplay(
                $"### Recent tracks for {StringExtensions.Sanitize(targetDisplayName)}");

            foreach (var track in trackPage)
            {
                container.WithSeparator();

                var cacheId = this._trackService.StoreScrobbleReference(
                    track.ArtistName, track.TrackName, track.AlbumName, track.TimePlayed);

                var alreadyScrobbled = (track.TimePlayed.HasValue &&
                                        alreadyScrobbledTimestamps.Contains(track.TimePlayed.Value)) ||
                                       this._trackService.IsTrackScrobbled(context.ContextUser.UserId,
                                           track.ArtistName, track.TrackName, track.TimePlayed);

                var button = new ButtonProperties(
                    $"{InteractionConstants.ScrobbleTrack}:{targetUserId}:{cacheId}",
                    alreadyScrobbled ? "Scrobbled" : "Scrobble",
                    alreadyScrobbled ? ButtonStyle.Success : ButtonStyle.Secondary);

                if (alreadyScrobbled)
                {
                    button.WithDisabled(true);
                }

                var section = new ComponentSectionProperties(button,
                    [new TextDisplayProperties(
                        StringService.TrackToLinkedStringWithTimestamp(track, context.ContextUser.RymEnabled))]);

                container.AddComponent(section);
            }

            container.WithSeparator();

            var footer = new StringBuilder();
            footer.Append($"-# {pageIndex + 1}/{trackPages.Count.Format(context.NumberFormat)}");
            footer.Append($" - {recentTracks.Count} total");
            container.WithTextDisplay(footer.ToString());

            container.WithActionRow(SupporterService.IsSupporter(context.ContextUser.UserType)
                ? StringService.GetPaginationActionRow(p)
                : StringService.GetSimplePaginationActionRow(p));

            var pageBuilder = new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container]);

            return pageBuilder.Build();
        }
    }

    public async Task<ResponseModel> ScrobbleFromReferenceAsync(
        ContextModel context,
        ReferencedMusic trackRef)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var commandExecutedCount = await this._userService.GetCommandExecutedAmount(context.ContextUser.UserId,
            "scrobble", DateTime.UtcNow.AddMinutes(-30));
        var maxCount = SupporterService.IsSupporter(context.ContextUser.UserType) ? 50 : 25;

        if (commandExecutedCount > maxCount)
        {
            response.ComponentsContainer.WithTextDisplay("Please wait before scrobbling to Last.fm again.");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        if (trackRef.TimePlayed.HasValue)
        {
            var hasDuplicate = await this._playService.HasPlayNearTimestamp(
                context.ContextUser.UserId, trackRef.TimePlayed.Value);

            if (hasDuplicate)
            {
                var dateValue = ((DateTimeOffset)DateTime.SpecifyKind(trackRef.TimePlayed.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                response.ComponentsContainer.WithTextDisplay($"You already have a scrobble near <t:{dateValue}:f>.");
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        var trackScrobbled = await this._dataSourceFactory.ScrobbleAsync(context.ContextUser.SessionKeyLastFm,
            trackRef.Artist, trackRef.Track, trackRef.Album, trackRef.TimePlayed);

        var recentTrack = new RecentTrack
        {
            TrackName = trackRef.Track,
            ArtistName = trackRef.Artist,
            AlbumName = trackRef.Album,
            TimePlayed = trackRef.TimePlayed,
            TrackUrl = LastfmUrlExtensions.GetTrackUrl(trackRef.Artist, trackRef.Track),
            ArtistUrl = LastfmUrlExtensions.GetArtistUrl(trackRef.Artist),
            AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(trackRef.Artist, trackRef.Album),
        };

        if (trackScrobbled.Success && trackScrobbled.Content.Accepted)
        {
            Statistics.LastfmScrobbles.Inc();
            this._trackService.MarkTrackAsScrobbled(context.ContextUser.UserId,
                trackRef.Artist, trackRef.Track, trackRef.TimePlayed);

            response.ComponentsContainer.WithTextDisplay($"### Scrobbled track for {userTitle}");
            response.ComponentsContainer.WithTextDisplay(
                StringService.TrackToLinkedStringWithTimestamp(recentTrack, context.ContextUser.RymEnabled));
        }
        else if (trackScrobbled.Success && trackScrobbled.Content.Ignored)
        {
            var description = new StringBuilder();
            description.AppendLine($"### Last.fm ignored scrobble for {userTitle}");

            if (!string.IsNullOrWhiteSpace(trackScrobbled.Content.IgnoreMessage))
            {
                description.AppendLine($"Reason: {trackScrobbled.Content.IgnoreMessage}");
            }

            description.AppendLine(
                StringService.TrackToLinkedStringWithTimestamp(recentTrack, context.ContextUser.RymEnabled));
            response.ComponentsContainer.WithTextDisplay(description.ToString());
        }
        else
        {
            response.ComponentsContainer.WithTextDisplay("Something went wrong while scrobbling track :(.");
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }
}
