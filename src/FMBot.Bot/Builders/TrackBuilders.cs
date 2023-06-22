using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
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
    private readonly LastFmRepository _lastFmRepository;
    private readonly PuppeteerService _puppeteerService;
    private readonly IUpdateService _updateService;
    private readonly SupporterService _supporterService;
    private readonly IIndexService _indexService;
    private readonly CensorService _censorService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly AlbumService _albumService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;

    public TrackBuilders(UserService userService,
        GuildService guildService,
        TrackService trackService,
        WhoKnowsTrackService whoKnowsTrackService,
        PlayService playService,
        SpotifyService spotifyService,
        TimeService timeService,
        LastFmRepository lastFmRepository,
        PuppeteerService puppeteerService,
        IUpdateService updateService,
        SupporterService supporterService,
        IIndexService indexService,
        CensorService censorService,
        WhoKnowsService whoKnowsService,
        AlbumService albumService,
        WhoKnowsPlayService whoKnowsPlayService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._trackService = trackService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._playService = playService;
        this._spotifyService = spotifyService;
        this._timeService = timeService;
        this._lastFmRepository = lastFmRepository;
        this._puppeteerService = puppeteerService;
        this._updateService = updateService;
        this._supporterService = supporterService;
        this._indexService = indexService;
        this._censorService = censorService;
        this._whoKnowsService = whoKnowsService;
        this._albumService = albumService;
        this._whoKnowsPlayService = whoKnowsPlayService;
    }

    public async Task<ResponseModel> TrackAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }
        response.EmbedAuthor.WithName($"Info about {trackSearch.Track.ArtistName} - {trackSearch.Track.TrackName} for {userTitle}");

        if (trackSearch.Track.TrackUrl != null)
        {
            response.EmbedAuthor.WithUrl(trackSearch.Track.TrackUrl);
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(trackSearch.Track);
        var leftStats = new StringBuilder();
        var rightStats = new StringBuilder();
        var footer = new StringBuilder();

        leftStats.AppendLine($"`{trackSearch.Track.TotalListeners}` listeners");
        leftStats.AppendLine($"`{trackSearch.Track.TotalPlaycount}` global {StringExtensions.GetPlaysString(trackSearch.Track.TotalPlaycount)}");
        leftStats.AppendLine($"`{trackSearch.Track.UserPlaycount}` {StringExtensions.GetPlaysString(trackSearch.Track.UserPlaycount)} by you");

        if (trackSearch.Track.UserPlaycount.HasValue)
        {
            await this._updateService.CorrectUserTrackPlaycount(context.ContextUser.UserId, trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.Value);
        }

        var duration = spotifyTrack?.DurationMs ?? trackSearch.Track.Duration;
        if (duration is > 0)
        {
            var trackLength = TimeSpan.FromMilliseconds(duration.GetValueOrDefault());
            var formattedTrackLength =
                $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

            rightStats.AppendLine($"`{formattedTrackLength}` duration");

            if (trackSearch.Track.UserPlaycount > 1)
            {
                var listeningTime =
                    await this._timeService.GetPlayTimeForTrackWithPlaycount(trackSearch.Track.ArtistName, trackSearch.Track.TrackName,
                        trackSearch.Track.UserPlaycount.GetValueOrDefault());

                leftStats.AppendLine($"`{StringExtensions.GetLongListeningTimeString(listeningTime)}` spent listening");
            }
        }

        if (spotifyTrack != null && !string.IsNullOrEmpty(spotifyTrack.SpotifyId))
        {
            var pitch = StringExtensions.KeyIntToPitchString(spotifyTrack.Key.GetValueOrDefault());

            rightStats.AppendLine($"`{pitch}` key");

            if (spotifyTrack.Tempo.HasValue)
            {
                var bpm = $"{spotifyTrack.Tempo.Value:0.0}";
                rightStats.AppendLine($"`{bpm}` bpm");
            }

            if (spotifyTrack.Danceability.HasValue && spotifyTrack.Energy.HasValue && spotifyTrack.Instrumentalness.HasValue &&
                spotifyTrack.Acousticness.HasValue && spotifyTrack.Speechiness.HasValue && spotifyTrack.Liveness.HasValue && spotifyTrack.Valence.HasValue)
            {
                var danceability = ((decimal)(spotifyTrack.Danceability / 1)).ToString("0%");
                var energetic = ((decimal)(spotifyTrack.Energy / 1)).ToString("0%");
                var instrumental = ((decimal)(spotifyTrack.Instrumentalness / 1)).ToString("0%");
                var acoustic = ((decimal)(spotifyTrack.Acousticness / 1)).ToString("0%");
                var speechful = ((decimal)(spotifyTrack.Speechiness / 1)).ToString("0%");
                var liveness = ((decimal)(spotifyTrack.Liveness / 1)).ToString("0%");
                var valence = ((decimal)(spotifyTrack.Valence / 1)).ToString("0%");
                footer.AppendLine($"{danceability} danceable - {energetic} energetic - {acoustic} acoustic\n" +
                                  $"{instrumental} instrumental - {speechful} speechful - {liveness} liveness\n" +
                                  $"{valence} valence (musical positiveness)");
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

                response.Embed.WithDescription($"Your first listen: <t:{firstListenValue}:D>");
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                response.Embed.WithDescription($"*Supporters can see the date they first listened to a track. " +
                                               $"[{Constants.GetSupporterOverviewButton}]({SupporterService.GetSupporterLink()})*");
            }
        }

        if (context.ContextUser.TotalPlaycount.HasValue && trackSearch.Track.UserPlaycount is >= 10)
        {
            footer.AppendLine($"{(decimal)trackSearch.Track.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value:P} of all your scrobbles are on this track");
        }

        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        response.Embed.AddField("Statistics", leftStats.ToString(), true);

        if (rightStats.Length > 0)
        {
            response.Embed.AddField("Info", rightStats.ToString(), true);
        }

        if (!string.IsNullOrWhiteSpace(trackSearch.Track.Description))
        {
            response.Embed.AddField("Summary", trackSearch.Track.Description);
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
        WhoKnowsMode mode,
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
            userId: context.ContextUser.UserId);
        if (track.Track == null)
        {
            return track.Response;
        }

        var cachedTrack = await this._spotifyService.GetOrStoreTrackAsync(track.Track);

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithTrack = await this._whoKnowsTrackService.GetIndexedUsersForTrack(context.DiscordGuild, guildUsers, guild.GuildId, track.Track.ArtistName, track.Track.TrackName);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.DiscordUser.Id);
        var currentUser = await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId), currentUser.UserId, guild);

        usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, context.ContextUser, trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var (filterStats, filteredUsersWithTrack) = WhoKnowsService.FilterWhoKnowsObjectsAsync(usersWithTrack, guild, roles);

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (mode == WhoKnowsMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"in <b>{context.DiscordGuild.Name}</b>", albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server);
        if (filteredUsersWithTrack.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this track.";
        }

        response.Embed.WithDescription(serverUsers);

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var footer = $"WhoKnows track requested by {userTitle}";

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.DiscordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
        {
            footer += $"\nMissing members? Update with {context.Prefix}refreshmembers";
        }

        if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
        {
            var serverListeners = filteredUsersWithTrack.Count;
            var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

            footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
            footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
            footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
        }

        if (filterStats.FullDescription != null)
        {
            footer += $"\n{filterStats.FullDescription}";
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
            guildUsers, guild, track.Track.ArtistName, track.Track.TrackName);

        if (guildAlsoPlaying != null)
        {
            footer += $"\n{guildAlsoPlaying}";
        }

        response.Embed.WithTitle(StringExtensions.TruncateLongString($"{trackName} in {context.DiscordGuild.Name}", 255));

        if (track.Track.TrackUrl != null)
        {
            response.Embed.WithUrl(track.Track.TrackUrl);
        }

        response.EmbedFooter.WithText(footer);
        response.Embed.WithFooter(response.EmbedFooter);

        if (displayRoleSelector)
        {
            if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
            {
                var allowedRoles = new SelectMenuBuilder()
                    .WithPlaceholder("Apply role filter..")
                    .WithCustomId($"{InteractionConstants.WhoKnowsTrackRolePicker}-{cachedTrack.Id}")
                    .WithType(ComponentType.RoleSelect)
                    .WithMinValues(0)
                    .WithMaxValues(25);

                response.Components = new ComponentBuilder().WithSelectMenu(allowedRoles);
            }
            else
            {
                //response.Components = new ComponentBuilder().WithButton(Constants.GetPremiumServer, disabled: true, customId: "1");
            }
        }

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowTrackAsync(
        ContextModel context,
        WhoKnowsMode mode,
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
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var track = await this._trackService.SearchTrack(response, context.DiscordUser, trackValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedTracks: true,
            userId: context.ContextUser.UserId);
        if (track.Track == null)
        {
            return track.Response;
        }

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var usersWithTrack = await this._whoKnowsTrackService.GetFriendUsersForTrack(context.DiscordGuild, guildUsers, guild?.GuildId ?? 0, context.ContextUser.UserId, track.Track.ArtistName, track.Track.TrackName);

        usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, context.ContextUser, trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (mode == WhoKnowsMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"from <b>{userTitle}</b>'s friends", albumCoverUrl, trackName,
                usersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"friends-whoknow-track-{track.Track.ArtistName}-{track.Track.TrackName}";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithTrack, context.ContextUser.UserId, PrivacyLevel.Server);
        if (!usersWithTrack.Any())
        {
            serverUsers = "None of your friends have listened to this track.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = "";

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer += $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
        }

        footer += $"\nFriends WhoKnow track requested by {userTitle}";

        if (usersWithTrack.Any() && usersWithTrack.Count() > 1)
        {
            var globalListeners = usersWithTrack.Count();
            var globalPlaycount = usersWithTrack.Sum(a => a.Playcount);
            var avgPlaycount = usersWithTrack.Average(a => a.Playcount);

            footer += $"\n{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ";
            footer += $"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ";
            footer += $"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}";
        }

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
            userId: context.ContextUser.UserId);
        if (track.Track == null)
        {
            return track.Response;
        }

        var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(track.Track);

        var trackName = $"{track.Track.TrackName} by {track.Track.ArtistName}";

        var usersWithTrack = await this._whoKnowsTrackService.GetGlobalUsersForTrack(context.DiscordGuild, track.Track.ArtistName, track.Track.TrackName);

        var filteredUsersWithTrack = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithTrack);

        filteredUsersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(filteredUsersWithTrack, context.ContextUser, trackName, context.DiscordGuild, track.Track.UserPlaycount);

        var privacyLevel = PrivacyLevel.Global;

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var footer = $"Global WhoKnows track requested by {userTitle}";

        if (settings.AdminView)
        {
            footer += "\nAdmin view enabled - not for public channels";
        }
        if (context.ContextUser.PrivacyLevel != PrivacyLevel.Global)
        {
            footer += $"\nYou are currently not globally visible - use '{context.Prefix}privacy global' to enable.";
        }
        if (settings.HidePrivateUsers)
        {
            footer += "\nAll private users are hidden from results";
        }

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
                footer += $"\n{guildAlsoPlaying}";
            }
        }

        string albumCoverUrl = null;
        if (track.Track.AlbumName != null)
        {
            albumCoverUrl = await GetAlbumCoverUrl(context, track, response);
        }

        if (settings.WhoKnowsMode == WhoKnowsMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows Track", $"in <b>.fmbot 🌐</b>", albumCoverUrl, trackName,
                filteredUsersWithTrack, context.ContextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"global-whoknows-track-{track.Track.ArtistName}-{track.Track.TrackName}";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, context.ContextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
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
            }
        }

        if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count() > 1)
        {
            var serverListeners = filteredUsersWithTrack.Count();
            var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

            footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
            footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
            footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
        }

        response.Embed.WithTitle(StringExtensions.TruncateLongString($"{trackName} globally", 255));

        if (!string.IsNullOrWhiteSpace(track.Track.TrackUrl))
        {
            response.Embed.WithUrl(track.Track.TrackUrl);
        }

        response.EmbedFooter.WithText(footer);
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
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                track.Track.AlbumName, track.Track.ArtistName, track.Track.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnailUrl(albumCoverUrl);
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

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, otherUserUsername: userSettings.UserNameLastFm, userId: context.ContextUser.UserId);
        if (trackSearch.Track == null)
        {
            return trackSearch.Response;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has `{trackSearch.Track.UserPlaycount}` {StringExtensions.GetPlaysString(trackSearch.Track.UserPlaycount)} " +
            $"for **{StringExtensions.Sanitize(trackSearch.Track.TrackName)}** by **{StringExtensions.Sanitize(trackSearch.Track.ArtistName)}**";

        if (trackSearch.Track.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserTrackPlaycount(context.ContextUser.UserId, trackSearch.Track.ArtistName,
                trackSearch.Track.TrackName, trackSearch.Track.UserPlaycount.Value);
        }

        if (!userSettings.DifferentUser && context.ContextUser.LastUpdated != null)
        {
            var playsLastWeek =
                await this._playService.GetWeekTrackPlaycountAsync(userSettings.UserId, trackSearch.Track.TrackName, trackSearch.Track.ArtistName);
            if (playsLastWeek != 0)
            {
                reply += $" (`{playsLastWeek}` last week)";
            }
        }

        response.Text = reply;

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

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
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
            var trackLoved = await this._lastFmRepository.LoveTrackAsync(context.ContextUser, trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

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

        var trackSearch = await this._trackService.SearchTrack(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
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
            var trackLoved = await this._lastFmRepository.UnLoveTrackAsync(context.ContextUser, trackSearch.Track.ArtistName, trackSearch.Track.TrackName);

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
            topGuildTracks = await this._whoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId, guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId, guildListSettings.AmountOfDaysWithBillboard);

            topGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
            previousTopGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
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

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue) ?
            $"Top {guildListSettings.TimeDescription.ToLower()} tracks in {context.DiscordGuild.Name}" :
            $"Top {guildListSettings.TimeDescription.ToLower()} '{guildListSettings.NewSearchValue}' tracks in {context.DiscordGuild.Name}";

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
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{track.ListenerCount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.TotalPlaycount} {StringExtensions.GetPlaysString(track.TotalPlaycount)})"
                    : $"`{track.TotalPlaycount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.ListenerCount} {StringExtensions.GetListenersString(track.ListenerCount)})";

                if (previousTopGuildTracks != null && previousTopGuildTracks.Any())
                {
                    var previousTopTrack = previousTopGuildTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                    int? previousPosition = previousTopTrack == null ? null : previousTopGuildTracks.IndexOf(previousTopTrack);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
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

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> TopTracksAsync(
        ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings)
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
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 200);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }
        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var previousTopTracks = new List<TopTrack>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousTopTracksCall = await this._lastFmRepository
                .GetTopTracksForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousTopTracksCall.Success)
            {
                previousTopTracks.AddRange(previousTopTracksCall.Content.TopTracks);
            }
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        var trackPages = topTracks.Content.TopTracks.ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var trackPage in trackPages)
        {
            var trackPageString = new StringBuilder();
            foreach (var track in trackPage)
            {
                var name = $"**{track.ArtistName}** - **[{track.TrackName}]({track.TrackUrl})** ({track.UserPlaycount} {StringExtensions.GetPlaysString(track.UserPlaycount)})";

                if (topListSettings.Billboard && previousTopTracks.Any())
                {
                    var previousTopTrack = previousTopTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                    int? previousPosition = previousTopTrack == null ? null : previousTopTracks.IndexOf(previousTopTrack);

                    trackPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                }
                else
                {
                    trackPageString.Append($"{counter}\\. ");
                    trackPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{trackPages.Count}");
            if (topTracks.Content.TotalAmount.HasValue)
            {
                footer.Append($" - {topTracks.Content.TotalAmount.Value} total tracks in this time period");
            }
            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append("View this list as a billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(trackPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
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

        var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
        response.EmbedAuthor.WithName($"Top {timeSettings.Description} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 20);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }
        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

        var image = await this._puppeteerService.GetReceipt(userSettings, topTracks.Content, timeSettings, count);

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.FileName = "receipt";

        return response;
    }


}

