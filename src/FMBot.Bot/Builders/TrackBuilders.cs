using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fergun.Interactive;
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

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName(
            $"Track: {trackSearch.Track.ArtistName} - {trackSearch.Track.TrackName} for {userTitle}");

        if (trackSearch.Track.TrackUrl != null)
        {
            response.EmbedAuthor.WithUrl(trackSearch.Track.TrackUrl);
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        var dbTrack = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track);
        var stats = new StringBuilder();
        var info = new StringBuilder();
        var footer = new StringBuilder();

        stats.AppendLine($"`{trackSearch.Track.TotalListeners.Format(context.NumberFormat)}` listeners");
        stats.AppendLine(
            $"`{trackSearch.Track.TotalPlaycount.Format(context.NumberFormat)}` global {StringExtensions.GetPlaysString(trackSearch.Track.TotalPlaycount)}");
        stats.AppendLine(
            $"`{trackSearch.Track.UserPlaycount.Format(context.NumberFormat)}` {StringExtensions.GetPlaysString(trackSearch.Track.UserPlaycount)} by you");

        if (trackSearch.Track.UserPlaycount.HasValue)
        {
            _ = this._updateService.CorrectUserTrackPlaycount(context.ContextUser.UserId,
                trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.Value);
        }

        var duration = dbTrack?.DurationMs ?? trackSearch.Track.Duration;
        if (duration is > 0)
        {
            var trackLength = TimeSpan.FromMilliseconds(duration.GetValueOrDefault());
            var formattedTrackLength =
                $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

            info.AppendLine($"`{formattedTrackLength}` duration");

            if (trackSearch.Track.UserPlaycount > 1)
            {
                var listeningTime =
                    await this._timeService.GetPlayTimeForTrackWithPlaycount(trackSearch.Track.ArtistName,
                        trackSearch.Track.TrackName,
                        trackSearch.Track.UserPlaycount.GetValueOrDefault());

                stats.AppendLine($"`{StringExtensions.GetLongListeningTimeString(listeningTime)}` spent listening");
            }
        }

        var audioFeatures = new StringBuilder();

        if (dbTrack != null && !string.IsNullOrEmpty(dbTrack.SpotifyId))
        {
            var pitch = StringExtensions.KeyIntToPitchString(dbTrack.Key.GetValueOrDefault());

            info.AppendLine($"`{pitch}` key");

            if (dbTrack.Tempo.HasValue)
            {
                var bpm = $"{dbTrack.Tempo.Value:0.0}";
                info.AppendLine($"`{bpm}` bpm");
            }

            if (dbTrack.Danceability.HasValue && dbTrack.Energy.HasValue &&
                dbTrack.Instrumentalness.HasValue &&
                dbTrack.Acousticness.HasValue && dbTrack.Speechiness.HasValue &&
                dbTrack.Liveness.HasValue && dbTrack.Valence.HasValue)
            {
                var danceability = ((decimal)(dbTrack.Danceability / 1)).ToString("0%");
                var energetic = ((decimal)(dbTrack.Energy / 1)).ToString("0%");
                var instrumental = ((decimal)(dbTrack.Instrumentalness / 1)).ToString("0%");
                var acoustic = ((decimal)(dbTrack.Acousticness / 1)).ToString("0%");
                var speechful = ((decimal)(dbTrack.Speechiness / 1)).ToString("0%");
                var liveness = ((decimal)(dbTrack.Liveness / 1)).ToString("0%");
                var valence = ((decimal)(dbTrack.Valence / 1)).ToString("0%");

                audioFeatures.AppendLine($"`{danceability}` danceable");
                audioFeatures.AppendLine($"`{energetic}` energetic");
                audioFeatures.AppendLine($"`{acoustic}` acoustic");
                audioFeatures.AppendLine($"`{instrumental}` instrumental");
                audioFeatures.AppendLine($"`{speechful}` speechful");
                audioFeatures.AppendLine($"`{liveness}` liveness");
                audioFeatures.AppendLine($"`{valence}` happy");
            }
        }

        if (context.ContextUser.UserType != UserType.User && trackSearch.Track.UserPlaycount > 0)
        {
            var firstPlay =
                await this._playService.GetTrackFirstPlayDate(context.ContextUser.UserId,
                    trackSearch.Track.ArtistName, trackSearch.Track.TrackName);
            if (firstPlay != null)
            {
                var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();

                response.Embed.WithDescription($"Discovered on: <t:{firstListenValue}:D>");
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
                response.Embed.WithDescription(
                    $"*[Supporters]({Constants.GetSupporterDiscordLink}) can see track discovery dates.*");
            }
        }

        if (trackSearch.IsRandom)
        {
            footer.AppendLine(
                $"Track #{trackSearch.RandomTrackPosition} ({trackSearch.RandomTrackPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(trackSearch.RandomTrackPlaycount)})");
        }

        var featuredHistory =
            await this._featuredService.GetTrackFeaturedHistory(trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName);
        if (featuredHistory.Any())
        {
            footer.AppendLine(
                $"Featured {featuredHistory.Count} {StringExtensions.GetTimesString(featuredHistory.Count)}");
        }

        if (context.ContextUser.TotalPlaycount.HasValue && trackSearch.Track.UserPlaycount is >= 10)
        {
            footer.AppendLine(
                $"{((decimal)trackSearch.Track.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat)} of all your plays are on this track");
        }

        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        if (audioFeatures.Length > 0)
        {
            response.Embed.AddField("Audio features", audioFeatures.ToString(), true);
        }

        response.Embed.AddField("Stats", stats.ToString(), true);

        if (info.Length > 0)
        {
            response.Embed.AddField("Info", info.ToString(), true);
        }

        response.Components = new ActionRowProperties();

        if (dbTrack?.SpotifyId != null)
        {
            var eurovisionEntry =
                await this._eurovisionService.GetEurovisionEntryForSpotifyId(dbTrack.SpotifyId);

            if (eurovisionEntry != null)
            {
                var eurovisionDescription = this._eurovisionService.GetEurovisionDescription(eurovisionEntry);
                response.Embed.AddField($"Eurovision <:eurovision:1084971471610323035> ", eurovisionDescription.full);
                if (eurovisionEntry.VideoLink != null)
                {
                    response.Components.WithButton(
                        emote: EmojiProperties.Custom(DiscordConstants.YouTube), url: eurovisionEntry.VideoLink);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(trackSearch.Track.Description))
        {
            response.Embed.AddField("Summary", trackSearch.Track.Description);
        }

        if (dbTrack?.SpotifyPreviewUrl != null)
        {
            response.Components.WithButton(
                    "Preview",
                    $"{InteractionConstants.TrackPreview}:{dbTrack.Id}",
                    style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PlayPreview));
        }

        if (SupporterService.IsSupporter(context.ContextUser.UserType) &&
            !string.IsNullOrWhiteSpace(dbTrack?.PlainLyrics))
        {
            response.Components.WithButton(
                "Lyrics",
                $"{InteractionConstants.TrackLyrics}:{dbTrack.Id}",
                style: ButtonStyle.Secondary,
                emote: EmojiProperties.Standard("🎤"));
        }

        //if (track.Tags != null && track.Tags.Any())
        //{
        //    var tags = LastFmRepository.TagsToLinkedString(track.Tags);

        //    response.Embed.AddField("Tags", tags);
        //}

        return response;
    }

    public async Task<ResponseModel> WhoKnowsTrackAsync(
        ContextModel context,
        ResponseMode mode,
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

        if (mode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track",
                $"in <b>{context.DiscordGuild.Name}</b>", albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

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

        response.Embed.WithTitle(
            StringExtensions.TruncateLongString($"{trackName} in {context.DiscordGuild.Name}", 255));

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
        ResponseMode mode,
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

        if (mode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"from <b>{userTitle}</b>'s friends",
                albumCoverUrl, trackName,
                usersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"friends-whoknow-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

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

        response.Embed.WithTitle($"{trackName} with friends");

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

        if (settings.ResponseMode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"in <b>.fmbot 🌐</b>",
                albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"global-whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}.png";
            response.ResponseType = ResponseType.ImageOnly;

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

        response.Embed.WithTitle(StringExtensions.TruncateLongString($"{trackName} globally", 255));

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

        if (spotifyTrack?.SpotifyPreviewUrl != null)
        {
            response.Components = new ActionRowProperties()
                .WithButton(
                    "Preview",
                    $"{InteractionConstants.TrackPreview}:{spotifyTrack.Id}",
                    style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PlayPreview));
        }

        return response;
    }

    public async Task<ResponseModel> TrackPreviewAsync(
        ContextModel context,
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

        var spotifyTrack = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track);

        try
        {
            await this._discordSkuService.SendVoiceMessage(spotifyTrack.SpotifyPreviewUrl, interactionToken);
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

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (trackSearch.Track.Loved)
        {
            response.Embed.WithTitle($"❤️ Track already loved");
            response.Embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track));
        }
        else
        {
            var trackLoved = await this._dataSourceFactory.LoveTrackAsync(context.ContextUser.SessionKeyLastFm,
                trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

            if (trackLoved)
            {
                response.Embed.WithTitle($"❤️ Loved track for {userTitle}");
                response.Embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track));
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

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (!trackSearch.Track.Loved)
        {
            response.Embed.WithTitle($"💔 Track wasn't loved");
            response.Embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track));
        }
        else
        {
            var trackLoved = await this._dataSourceFactory.UnLoveTrackAsync(context.ContextUser.SessionKeyLastFm,
                trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

            if (trackLoved)
            {
                response.Embed.WithTitle($"💔 Unloved track for {userTitle}");
                response.Embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(trackSearch.Track));
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
        IList<GuildTrack> previousTopGuildTracks = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildTracks = await this._whoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId,
                guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId,
                guildListSettings.AmountOfDaysWithBillboard);

            topGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.StartDateTime,
                guildListSettings.OrderType, guildListSettings.NewSearchValue);
            previousTopGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.BillboardStartDateTime,
                guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }

        if (!topGuildTracks.Any())
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

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific track listeners with '{context.Prefix}whoknowstrack'");
                break;
            case 2:
                footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                break;
            case 3:
                footer.AppendLine($"Available sorting options: plays and listeners");
                break;
        }

        var trackPages = topGuildTracks.Chunk(12).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in trackPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in page)
            {
                var trackName = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue) ? $"**{track.ArtistName}** - **{track.TrackName}**" : $"**{track.TrackName}**";
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

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{trackPages.Count}");
            pageFooter.Append(footer);

            pages.Add(new PageBuilder()
                .WithTitle(title)
                .WithDescription(pageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
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

            var image = await this._puppeteerService.GetTopList(userTitle, "Top Tracks", "tracks",
                timeSettings.Description,
                topTracks.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), backgroundImage,
                topTracks.TopList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
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
        response.EmbedAuthor.WithName($"Top {timeSettings.Description} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

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

        var image = await this._puppeteerService.GetReceipt(userSettings, topTracks.Content, timeSettings, count);

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.FileName = "receipt.png";

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
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.Embed.WithTitle($"{context.Prefix}scrobble");
            response.Embed.WithDescription(
                "Scrobbles a track. You can enter a search value or enter the exact name with separators. " +
                "You can only scrobble tracks that already exist on Last.fm.");

            response.Embed.AddField("Search for a track to scrobble",
                $"Format: `{context.Prefix}scrobble SearchValue`\n" +
                $"`{context.Prefix}sb the less i know the better` *(scrobbles The Less I Know The Better by Tame Impala)*\n" +
                $"`{context.Prefix}scrobble Loona Heart Attack` *(scrobbles Heart Attack (츄) by LOONA)*");

            response.Embed.AddField("Or enter the exact name with separators",
                $"Format: `{context.Prefix}scrobble Artist | Track`\n" +
                $"`{context.Prefix}scrobble Mac DeMarco | Chamber of Reflection`\n" +
                $"`{context.Prefix}scrobble Home | Climbing Out`");

            response.Embed.AddField("You can also specify the album",
                $"Format: `{context.Prefix}scrobble Artist | Track | Album`\n" +
                $"`{context.Prefix}scrobble Mac DeMarco | Chamber of Reflection | Salad Days`\n");

            response.Embed.AddField("Or use a Discogs link",
                $"`{context.Prefix}scrobble https://www.discogs.com/release/249504-Rick-Astley-Never-Gonna-Give-You-Up`");

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
        }
        else
        {
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
                response.Embed.WithDescription("Something went wrong while scrobbling track :(.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.Error;
            }
        }

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

        response.EmbedAuthor.WithName(
            $"Lyrics for {trackSearch.Track.ArtistName} - {trackSearch.Track.TrackName}");

        if (trackSearch.Track.TrackUrl != null)
        {
            response.EmbedAuthor.WithUrl(trackSearch.Track.TrackUrl);
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        var track = await this._musicDataFactory.GetOrStoreTrackAsync(trackSearch.Track, true);

        if (track == null || track.PlainLyrics == null)
        {
            response.Embed.WithDescription("Sorry, we don't have the lyrics for this track.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var allLines = track.PlainLyrics.Split('\n').ToList();
        var lyricsPages = new List<LyricPage>();
        const int linesPerPage = 20;

        var syncedLyricIndex = 0;

        for (var i = 0; i < allLines.Count; i += linesPerPage)
        {
            var pageLines = allLines.Skip(i).Take(linesPerPage);
            var pageContent = string.Join("\n", pageLines);

            TimeSpan? start = null;
            TimeSpan? end = null;

            if (track.SyncedLyrics != null && track.SyncedLyrics.Any())
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
                        continue;
                    }

                    var syncedLine = track.SyncedLyrics.ElementAtOrDefault(syncedLyricIndex);
                    if (syncedLine == null)
                    {
                        break;
                    }

                    var closeness = GameService.GetLevenshteinDistance(syncedLine.Text, line);
                    if (closeness > 2)
                    {
                        syncedLyricIndex++;
                        syncedLine = track.SyncedLyrics.ElementAtOrDefault(syncedLyricIndex);
                        if (syncedLine == null)
                        {
                            break;
                        }

                        end = syncedLine.Timestamp;
                    }
                    else
                    {
                        end = syncedLine.Timestamp;
                        syncedLyricIndex++;
                    }

                }

            }

            lyricsPages.Add(new LyricPage(pageContent, start, end));
        }

        var pages = new List<PageBuilder>();
        for (var i = 0; i < lyricsPages.Count; i++)
        {
            var footer = new StringBuilder();

            footer.Append($"Page {i + 1}/{lyricsPages.Count}");
            var lyricPage = lyricsPages[i];

            if (lyricPage.Start.HasValue && lyricPage.End.HasValue)
            {
                footer.Append($" — {StringExtensions.GetTrackLength(lyricPage.Start.Value)} until {StringExtensions.GetTrackLength(lyricPage.End.Value.Add(TimeSpan.FromSeconds(3)))}");
            }
            else if (track.DurationMs.HasValue)
            {
                footer.Append($" — {StringExtensions.GetTrackLength(track.DurationMs.Value)}");
            }

            var page = new PageBuilder()
                .WithDescription(lyricPage.Text)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString());

            if (response.Embed.Color != null)
            {
                page.WithColor(response.Embed.Color);
            }

            pages.Add(page);
        }

        if (pages.Count == 1)
        {
            response.ResponseType = ResponseType.Embed;
            response.SinglePageToEmbedResponseWithButton(pages.First());
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = StringService.BuildSimpleComponentPaginator(pages);
        }

        response.CommandResponse = CommandResponse.Ok;
        return response;
    }

    private record LyricPage(string Text, TimeSpan? Start, TimeSpan? End);

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
}
