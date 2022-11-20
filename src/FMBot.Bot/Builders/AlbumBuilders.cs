using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
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
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;

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
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly SupporterService _supporterService;

    public AlbumBuilders(UserService userService,
        GuildService guildService,
        AlbumService albumService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        PlayService playService,
        SpotifyService spotifyService,
        TrackService trackService,
        IUpdateService updateService,
        TimeService timeService,
        CensorService censorService,
        LastFmRepository lastFmRepository,
        SupporterService supporterService)
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
        this._lastFmRepository = lastFmRepository;
        this._supporterService = supporterService;
    }

    public async Task<ResponseModel> AlbumAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(albumSearch.Album);
        databaseAlbum.Tracks = await this._spotifyService.GetExistingAlbumTracks(databaseAlbum.Id);

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        response.EmbedAuthor.WithName(
            StringExtensions.TruncateLongString($"Info about {albumSearch.Album.ArtistName} - {albumSearch.Album.AlbumName} for {userTitle}", 255));

        if (albumSearch.Album.AlbumUrl != null)
        {
            response.Embed.WithUrl(albumSearch.Album.AlbumUrl);
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        if (databaseAlbum.ReleaseDate != null)
        {
            response.Embed.WithDescription($"Release date: `{databaseAlbum.ReleaseDate}`");
        }

        var artistUserTracks = await this._trackService.GetArtistUserTracks(context.ContextUser.UserId, albumSearch.Album.ArtistName);

        var globalStats = new StringBuilder();
        globalStats.AppendLine($"`{albumSearch.Album.TotalListeners}` {StringExtensions.GetListenersString(albumSearch.Album.TotalListeners)}");
        globalStats.AppendLine($"`{albumSearch.Album.TotalPlaycount}` global {StringExtensions.GetPlaysString(albumSearch.Album.TotalPlaycount)}");
        if (albumSearch.Album.UserPlaycount.HasValue)
        {
            globalStats.AppendLine($"`{albumSearch.Album.UserPlaycount}` {StringExtensions.GetPlaysString(albumSearch.Album.UserPlaycount)} by you");
            globalStats.AppendLine($"`{await this._playService.GetWeekAlbumPlaycountAsync(context.ContextUser.UserId, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName)}` by you last week");
            await this._updateService.CorrectUserAlbumPlaycount(context.ContextUser.UserId, albumSearch.Album.ArtistName,
                albumSearch.Album.AlbumName, albumSearch.Album.UserPlaycount.Value);
        }

        if (albumSearch.Album.UserPlaycount.HasValue && albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Any() && artistUserTracks.Any())
        {
            var listeningTime = await this._timeService.GetPlayTimeForAlbum(albumSearch.Album.AlbumTracks, artistUserTracks,
                albumSearch.Album.UserPlaycount.Value);
            globalStats.AppendLine($"`{StringExtensions.GetLongListeningTimeString(listeningTime)}` spent listening");
        }

        response.Embed.AddField("Statistics", globalStats.ToString(), true);

        if (context.DiscordGuild != null)
        {
            var serverStats = "";
            var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);

            if (guild?.LastIndexed != null)
            {
                var usersWithAlbum = await this._whoKnowsAlbumService.GetIndexedUsersForAlbum(context.DiscordGuild, guild.GuildId, albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
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
                serverStats += $"Run `{context.Prefix}index` to get server stats";
            }

            response.Embed.AddField("Server stats", serverStats, true);
        }

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }
        if (albumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnailUrl(albumCoverUrl);
            }
        }

        if (context.ContextUser.UserType != UserType.User && albumSearch.Album.UserPlaycount > 0)
        {
            var firstPlay =
                await this._playService.GetAlbumFirstPlayDate(context.ContextUser.UserId,
                    albumSearch.Album.ArtistName, albumSearch.Album.AlbumName);
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
                response.Embed.WithDescription($"*Want to see the date you first listened to this album? " +
                                               $"[Get .fmbot supporter here.]({Constants.GetSupporterLink})*");
            }
        }

        var footer = new StringBuilder();

        if (context.ContextUser.TotalPlaycount.HasValue && albumSearch.Album.UserPlaycount is >= 10)
        {
            footer.AppendLine($"{(decimal)albumSearch.Album.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value:P} of all your scrobbles are on this album");
        }

        if (databaseAlbum?.Label != null)
        {
            footer.AppendLine($"Label: {databaseAlbum.Label}");
        }

        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        if (albumSearch.Album.Description != null)
        {
            response.Embed.AddField("Summary", albumSearch.Album.Description);
        }

        if (albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Any())
        {
            var trackDescription = new StringBuilder();

            for (var i = 0; i < albumSearch.Album.AlbumTracks.Count; i++)
            {
                var track = albumSearch.Album.AlbumTracks.OrderBy(o => o.Rank).ToList()[i];

                var albumTrackWithPlaycount = artistUserTracks.FirstOrDefault(f =>
                    StringExtensions.SanitizeTrackNameForComparison(track.TrackName)
                        .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));

                trackDescription.Append(
                    $"{i + 1}.");

                trackDescription.Append(
                    $" **{track.TrackName}**");

                if (albumTrackWithPlaycount != null)
                {
                    trackDescription.Append(
                        $" - *{albumTrackWithPlaycount.Playcount} {StringExtensions.GetPlaysString(albumTrackWithPlaycount.Playcount)}*");
                }

                if (track.Duration.HasValue)
                {
                    trackDescription.Append(albumTrackWithPlaycount == null ? " — " : " - ");

                    var duration = TimeSpan.FromSeconds(track.Duration.Value);
                    var formattedTrackLength =
                        $"{(duration.Hours == 0 ? "" : $"{duration.Hours}:")}{duration.Minutes}:{duration.Seconds:D2}";
                    trackDescription.Append($"`{formattedTrackLength}`");
                }

                trackDescription.AppendLine();

                if (trackDescription.Length > 900 && (albumSearch.Album.AlbumTracks.Count - 2 - i) > 1)
                {
                    trackDescription.Append($"*And {albumSearch.Album.AlbumTracks.Count - 2 - i} more tracks (view all with `{context.Prefix}albumtracks`)*");
                    break;
                }
            }
            response.Embed.AddField("Tracks", trackDescription.ToString());
        }

        if (context.ContextUser.UserDiscogs != null && context.ContextUser.DiscogsReleases.Any())
        {
            var albumCollection = context.ContextUser.DiscogsReleases.Where(w =>
                (w.Release.Title.ToLower().Contains(albumSearch.Album.AlbumName.ToLower()) ||
                 albumSearch.Album.AlbumName.ToLower().Contains(w.Release.Title))
                &&
                w.Release.Artist.ToLower().Contains(albumSearch.Album.ArtistName.ToLower())).ToList();

            if (albumCollection.Any())
            {
                var albumCollectionDescription = new StringBuilder();
                foreach (var album in albumCollection.Take(4))
                {
                    albumCollectionDescription.Append(StringService.UserDiscogsReleaseToString(album));
                }
                response.Embed.AddField("Your Discogs collection", albumCollectionDescription.ToString());
            }
        }

        //if (album.Tags != null && album.Tags.Any())
        //{
        //    var tags = LastFmRepository.TagsToLinkedString(album.Tags);

        //    response.Embed.AddField("Tags", tags);
        //}

        response.Embed.WithFooter(footer.ToString());
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
            topGuildAlbums = await this._whoKnowsAlbumService.GetTopAllTimeAlbumsForGuild(guild.GuildId, guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId, guildListSettings.AmountOfDaysWithBillboard);

            topGuildAlbums = PlayService.GetGuildTopAlbums(plays, guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
            previousTopGuildAlbums = PlayService.GetGuildTopAlbums(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
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

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue) ?
            $"Top {guildListSettings.TimeDescription.ToLower()} albums in {context.DiscordGuild.Name}" :
            $"Top {guildListSettings.TimeDescription.ToLower()} '{guildListSettings.NewSearchValue}' albums in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var randomHintNumber = new Random().Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific track listeners with '{context.Prefix}whoknowsalbum'");
                break;
            case 2:
                footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                break;
            case 3:
                footer.AppendLine($"Available sorting options: plays and listeners");
                break;
        }

        var albumPages = topGuildAlbums.Chunk(12).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in albumPages)
        {
            var pageString = new StringBuilder();
            foreach (var album in page)
            {
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{album.ListenerCount}` · **{album.ArtistName}** - **{album.AlbumName}** ({album.TotalPlaycount} {StringExtensions.GetPlaysString(album.TotalPlaycount)})"
                    : $"`{album.TotalPlaycount}` · **{album.ArtistName}** - **{album.AlbumName}** ({album.ListenerCount} {StringExtensions.GetListenersString(album.ListenerCount)})";

                if (previousTopGuildAlbums != null && previousTopGuildAlbums.Any())
                {
                    var previousTopAlbum = previousTopGuildAlbums.FirstOrDefault(f => f.ArtistName == album.ArtistName && f.AlbumName == album.AlbumName);
                    int? previousPosition = previousTopAlbum == null ? null : previousTopGuildAlbums.IndexOf(previousTopAlbum);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{albumPages.Count}");
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

    public async Task<ResponseModel> AlbumTracksAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var albumName = $"{albumSearch.Album.AlbumName} by {albumSearch.Album.ArtistName}";

        var spotifySource = false;

        List<AlbumTrack> albumTracks;
        if (albumSearch.Album.AlbumTracks != null && albumSearch.Album.AlbumTracks.Any())
        {
            albumTracks = albumSearch.Album.AlbumTracks;
        }
        else
        {
            var dbAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(albumSearch.Album);
            dbAlbum.Tracks = await this._spotifyService.GetExistingAlbumTracks(dbAlbum.Id);

            if (dbAlbum?.Tracks != null && dbAlbum.Tracks.Any())
            {
                albumTracks = dbAlbum.Tracks.Select(s => new AlbumTrack
                {
                    TrackName = s.Name,
                    ArtistName = albumSearch.Album.ArtistName,
                    Duration = s.DurationMs / 1000
                }).ToList();
                spotifySource = true;
            }
            else
            {
                response.Embed.WithDescription(
                    $"Sorry, but neither Last.fm or Spotify know the tracks for {albumName}.");
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }
        }

        var artistUserTracks = await this._trackService.GetArtistUserTracks(userSettings.UserId, albumSearch.Album.ArtistName);

        var description = new StringBuilder();
        var amountOfDiscs = albumTracks.Count(c => c.Rank == 1) == 0 ? 1 : albumTracks.Count(c => c.Rank == 1);

        var pages = new List<PageBuilder>();

        var footer = new StringBuilder();

        footer.AppendLine($"{albumTracks.Count} total tracks");
        footer.Append(spotifySource ? "Album source: Spotify | " : "Album source: Last.fm | ");
        footer.Append($"{userSettings.DiscordUserName} has {albumSearch.Album.UserPlaycount} total scrobbles on this album");

        var url = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/music/" +
                  $"{UrlEncoder.Default.Encode(albumSearch.Album.ArtistName)}/" +
                  $"{UrlEncoder.Default.Encode(albumSearch.Album.AlbumName)}/";

        var i = 0;
        var tracksDisplayed = 0;
        var pageNumber = 1;
        for (var disc = 1; disc < amountOfDiscs + 1; disc++)
        {
            if (amountOfDiscs > 1)
            {
                description.AppendLine($"`Disc {disc}`");
            }

            for (; i < albumTracks.Count; i++)
            {
                var albumTrack = albumTracks[i];

                var albumTrackWithPlaycount = artistUserTracks.FirstOrDefault(f =>
                    StringExtensions.SanitizeTrackNameForComparison(albumTrack.TrackName)
                        .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));

                description.Append(
                    $"{i + 1}.");

                description.Append(
                    $" **{albumTrack.TrackName}**");

                if (albumTrackWithPlaycount != null)
                {
                    description.Append(
                        $" - *{albumTrackWithPlaycount.Playcount} {StringExtensions.GetPlaysString(albumTrackWithPlaycount.Playcount)}*");
                }

                if (albumTrack.Duration.HasValue)
                {
                    description.Append(albumTrackWithPlaycount == null ? " — " : " - ");

                    var duration = TimeSpan.FromSeconds(albumTrack.Duration.Value);
                    var formattedTrackLength =
                        $"{(duration.Hours == 0 ? "" : $"{duration.Hours}:")}{duration.Minutes}:{duration.Seconds:D2}";
                    description.Append($"`{formattedTrackLength}`");
                }

                description.AppendLine();

                var pageNumberDesc = $"Page {pageNumber}/{albumTracks.ChunkBy(12).Count} - ";

                tracksDisplayed++;
                if (tracksDisplayed > 0 && tracksDisplayed % 12 == 0 || tracksDisplayed == albumTracks.Count)
                {
                    var page = new PageBuilder()
                        .WithDescription(description.ToString())
                        .WithTitle($"Track playcounts for {albumName}")
                        .WithFooter(pageNumberDesc + footer);

                    if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        page.WithUrl(url);
                    }

                    pages.Add(page);
                    description = new StringBuilder();
                    pageNumber++;
                }
            }
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
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

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, otherUserUsername: userSettings.UserNameLastFm, userId: context.ContextUser.UserId);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var reply =
            $"**{Format.Sanitize(userSettings.DiscordUserName)}{userSettings.UserType.UserTypeToIcon()}** has `{albumSearch.Album.UserPlaycount}` {StringExtensions.GetPlaysString(albumSearch.Album.UserPlaycount)} " +
            $"for **{Format.Sanitize(albumSearch.Album.AlbumName)}** by **{Format.Sanitize(albumSearch.Album.ArtistName)}**";

        if (albumSearch.Album.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserAlbumPlaycount(context.ContextUser.UserId, albumSearch.Album.ArtistName,
                albumSearch.Album.AlbumName, albumSearch.Album.UserPlaycount.Value);
        }

        if (!userSettings.DifferentUser && context.ContextUser.LastUpdated != null)
        {
            var playsLastWeek =
                await this._playService.GetWeekAlbumPlaycountAsync(userSettings.UserId, albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            if (playsLastWeek != 0)
            {
                reply += $" (`{playsLastWeek}` last week)";
            }
        }

        response.Text = reply;

        return response;
    }

    public async Task<ResponseModel> CoverAsync(
        ContextModel context,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed,
        };

        var albumSearch = await this._albumService.SearchAlbum(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            useCachedAlbums: true, userId: context.ContextUser.UserId);
        if (albumSearch.Album == null)
        {
            return albumSearch.Response;
        }

        var databaseAlbum = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(albumSearch.Album, true);

        var albumCoverUrl = albumSearch.Album.AlbumCoverUrl;
        if (databaseAlbum.SpotifyImageUrl != null)
        {
            albumCoverUrl = databaseAlbum.SpotifyImageUrl;
        }

        if (albumCoverUrl == null)
        {
            response.Embed.WithDescription("Sorry, no album cover found for this album: \n" +
                                        $"{albumSearch.Album.ArtistName} - {albumSearch.Album.AlbumName}\n" +
                                        $"[View on last.fm]({albumSearch.Album.AlbumUrl})");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
            albumSearch.Album.AlbumName, albumSearch.Album.ArtistName, albumSearch.Album.AlbumUrl, response.Embed);

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            response.CommandResponse = CommandResponse.Censored;
            return response;
        }

        var image = await this._lastFmRepository.GetAlbumImageAsStreamAsync(albumCoverUrl);
        if (image == null)
        {
            response.Embed.WithDescription("Sorry, something went wrong while getting album cover for this album: \n" +
                                        $"{albumSearch.Album.ArtistName} - {albumSearch.Album.AlbumName}\n" +
                                        $"[View on last.fm]({albumSearch.Album.AlbumUrl})");
            response.CommandResponse = CommandResponse.Error;
            return response;
        }

        var cacheStream = new MemoryStream();
        await image.CopyToAsync(cacheStream);
        image.Position = 0;

        var description = new StringBuilder();
        description.AppendLine($"**{albumSearch.Album.ArtistName} - [{albumSearch.Album.AlbumName}]({albumSearch.Album.AlbumUrl})**");

        if (safeForChannel == CensorService.CensorResult.Nsfw)
        {
            description.AppendLine("⚠️ NSFW - Click to reveal");
        }

        response.Embed.WithDescription(description.ToString());
        response.EmbedFooter.WithText(
            $"Album cover requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}");

        response.Embed.WithFooter(response.EmbedFooter);
        response.Stream = image;
        response.FileName =
            $"cover-{StringExtensions.ReplaceInvalidChars($"{albumSearch.Album.ArtistName}_{albumSearch.Album.AlbumName}")}";
        response.Spoiler = safeForChannel == CensorService.CensorResult.Nsfw;


        var cacheFilePath = ChartService.AlbumUrlToCacheFilePath(albumSearch.Album.AlbumUrl);
        await ChartService.OverwriteCache(cacheStream, cacheFilePath);

        await cacheStream.DisposeAsync();

        return response;
    }

    public async Task<ResponseModel> TopAlbumsAsync(
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
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        if (!userSettings.DifferentUser && !context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        var userUrl =
            $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{timeSettings.UrlParameter}";

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} albums for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        const int amount = 200;

        var albums = await this._lastFmRepository.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings, amount);
        if (!albums.Success || albums.Content == null)
        {
            response.Embed.ErrorResponse(albums.Error, albums.Message, "top albums", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }
        if (albums.Content?.TopAlbums == null || !albums.Content.TopAlbums.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top albums in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var previousTopAlbums = new List<TopAlbum>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousAlbumsCall = await this._lastFmRepository
                .GetTopAlbumsForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, amount);

            if (previousAlbumsCall.Success)
            {
                previousTopAlbums.AddRange(previousAlbumsCall.Content.TopAlbums);
            }
        }

        var albumPages = albums.Content.TopAlbums
            .ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

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

                var name = $"**{album.ArtistName}** - **[{escapedAlbumName}]({url})** ({album.UserPlaycount} {StringExtensions.GetPlaysString(album.UserPlaycount)})";

                if (topListSettings.Billboard && previousTopAlbums.Any())
                {
                    var previousTopAlbum = previousTopAlbums.FirstOrDefault(f => f.ArtistName == album.ArtistName && f.AlbumName == album.AlbumName);
                    int? previousPosition = previousTopAlbum == null ? null : previousTopAlbums.IndexOf(previousTopAlbum);

                    albumPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                }
                else
                {
                    albumPageString.Append($"{counter}. ");
                    albumPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{albumPages.Count}");
            if (albums.Content.TotalAmount.HasValue && albums.Content.TotalAmount.Value != amount)
            {
                footer.Append($" - {albums.Content.TotalAmount} different albums in this time period");
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
                .WithDescription(albumPageString.ToString())
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

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }
}
