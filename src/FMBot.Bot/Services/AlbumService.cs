using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using Npgsql;
using Serilog;
using SkiaSharp;
using Web.InternalApi;

namespace FMBot.Bot.Services;

public class AlbumService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SpotifyService _spotifyService;
    private readonly AppleMusicService _appleMusicService;
    private readonly TimerService _timer;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly UpdateService _updateService;
    private readonly AliasService _aliasService;
    private readonly UserService _userService;
    private readonly AlbumEnrichment.AlbumEnrichmentClient _albumEnrichment;
    private readonly FeaturedService _featuredService;
    private readonly Core.AlbumFilterService _albumFilterService;

    public AlbumService(IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory,
        SpotifyService spotifyService,
        AppleMusicService appleMusicService,
        TimerService timer,
        WhoKnowsAlbumService whoKnowsAlbumService,
        IDbContextFactory<FMBotDbContext> contextFactory,
        UpdateService updateService,
        AliasService aliasService,
        UserService userService,
        AlbumEnrichment.AlbumEnrichmentClient albumEnrichment,
        FeaturedService featuredService,
        Core.AlbumFilterService albumFilterService)
    {
        this._cache = cache;
        this._dataSourceFactory = dataSourceFactory;
        this._spotifyService = spotifyService;
        this._appleMusicService = appleMusicService;
        this._timer = timer;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._contextFactory = contextFactory;
        this._updateService = updateService;
        this._aliasService = aliasService;
        this._userService = userService;
        this._albumEnrichment = albumEnrichment;
        this._featuredService = featuredService;
        this._albumFilterService = albumFilterService;
        this._botSettings = botSettings.Value;
    }

    public async Task<AlbumSearch> SearchAlbum(ResponseModel response, NetCord.User discordUser, Localizer localizer,
        string albumValues, string lastFmUserName, string sessionKey = null,
        string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null, ulong? interactionId = null,
        RestMessage referencedMessage = null, bool redirectsEnabled = true, ulong? discordGuildId = null)
    {
        string searchValue;
        if (referencedMessage != null && string.IsNullOrWhiteSpace(albumValues))
        {
            var internalLookup = CommandContextExtensions.GetReferencedMusic(referencedMessage.Id)
                                 ??
                                 await this._userService.GetReferencedMusic(referencedMessage.Id);

            if (internalLookup?.Album != null)
            {
                albumValues = $"{internalLookup.Artist} | {internalLookup.Album}";
            }
        }

        if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.Length != 0)
        {
            searchValue = albumValues;

            if (searchValue.ToLower() == "featured")
            {
                var featured = this._timer.CurrentFeatured;
                if (discordGuildId.HasValue)
                {
                    featured = await this._featuredService.GetCurrentGuildFeatured(discordGuildId.Value) ?? featured;
                }

                searchValue = $"{featured.ArtistName} | {featured.AlbumName}";
            }

            int? rndPosition = null;
            long? rndPlaycount = null;
            if (userId.HasValue && (albumValues.ToLower() == "rnd" || albumValues.ToLower() == "random"))
            {
                var topAlbums = await this.GetUserAllTimeTopAlbums(userId.Value, true);
                if (topAlbums.Count > 0)
                {
                    var rnd = RandomNumberGenerator.GetInt32(0, topAlbums.Count);

                    var album = topAlbums[rnd];

                    rndPosition = rnd;
                    rndPlaycount = album.UserPlaycount;
                    searchValue = $"{album.ArtistName} | {album.AlbumName}";
                }
            }

            var resolvedAlbumFromLink = await ResolveAlbumFromLink(searchValue);
            if (resolvedAlbumFromLink != null)
            {
                searchValue = resolvedAlbumFromLink;
            }

            if (searchValue.Contains(" | "))
            {
                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var searchArtistName = searchValue.Split(" | ")[0];
                var searchAlbumName = searchValue.Split(" | ")[1];

                Response<AlbumInfo> albumInfo;
                if (useCachedAlbums)
                {
                    albumInfo = await GetCachedAlbum(searchArtistName, searchAlbumName, lastFmUserName, userId,
                        redirectsEnabled);
                }
                else
                {
                    albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(searchArtistName, searchAlbumName,
                        lastFmUserName);
                }

                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = searchArtistName,
                    Album = searchAlbumName
                };

                if (!albumInfo.Success && albumInfo.Error == ResponseStatus.MissingParameters)
                {
                    var desc = localizer.Translate("album.notFoundWithValues",
                        ("album", searchAlbumName), ("artist", searchArtistName));
                    response.Embed.WithDescription(desc);
                    response.ComponentsContainer.WithTextDisplay(desc);
                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return new AlbumSearch(null, response);
                }

                if (!albumInfo.Success || albumInfo.Content == null)
                {
                    response.Embed.ErrorResponse(albumInfo.Error, albumInfo.Message, null, localizer, discordUser, "album");
                    response.ComponentsContainer.WithTextDisplay(response.Embed.Description ?? localizer.Translate("album.infoFailed"));
                    response.CommandResponse = CommandResponse.LastFmError;
                    response.ResponseType = ResponseType.Embed;
                    return new AlbumSearch(null, response);
                }

                return new AlbumSearch(albumInfo.Content, response, rndPosition, rndPlaycount);
            }
        }
        else
        {
            Response<RecentTrackList> recentScrobbles;

            if (userId.HasValue && otherUserUsername == null)
            {
                recentScrobbles = await this._updateService.UpdateUser(new UpdateUserQueueItem(userId.Value,
                    getAccurateTotalPlaycount: false));
            }
            else
            {
                recentScrobbles =
                    await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                var errorResponse =
                    GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, lastFmUserName, localizer);
                return new AlbumSearch(null, errorResponse);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            if (string.IsNullOrWhiteSpace(lastPlayedTrack.AlbumName))
            {
                var desc = localizer.Translate("album.noAlbumOnTrack",
                    ("track", lastPlayedTrack.TrackName), ("artist", lastPlayedTrack.ArtistName));
                response.Embed.WithDescription(desc);
                response.ComponentsContainer.WithTextDisplay(desc);

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName, lastFmUserName,
                    userId);
            }
            else
            {
                albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(lastPlayedTrack.ArtistName,
                    lastPlayedTrack.AlbumName,
                    lastFmUserName);
            }

            response.ReferencedMusic = new ReferencedMusic
            {
                Artist = lastPlayedTrack.ArtistName,
                Album = lastPlayedTrack.AlbumName
            };

            if (albumInfo?.Content == null || !albumInfo.Success)
            {
                var desc = localizer.Translate("album.noLastFmResult",
                    ("album", lastPlayedTrack.AlbumName), ("artist", lastPlayedTrack.ArtistName));
                response.Embed.WithDescription(desc);
                response.ComponentsContainer.WithTextDisplay(desc);

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            return new AlbumSearch(albumInfo.Content, response, latestScrobble: lastPlayedTrack);
        }

        var albumSearch = await this.SearchAlbumInDatabase(searchValue, userId);
        if (albumSearch != null)
        {

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(albumSearch.ArtistName, albumSearch.Name, lastFmUserName, userId);
            }
            else
            {
                albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(albumSearch.ArtistName, albumSearch.Name,
                    lastFmUserName);
            }

            if (albumInfo?.Content != null && interactionId is not null)
            {
                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = albumInfo.Content.ArtistName,
                    Album = albumInfo.Content.AlbumName
                };
            }

            if (albumInfo?.Content == null || !albumInfo.Success)
            {
                response.Embed.ErrorResponse(albumInfo.Error, albumInfo.Message, null, localizer, discordUser, "album");
                response.ComponentsContainer.WithTextDisplay(response.Embed.Description ?? localizer.Translate("album.infoFailed"));
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            return new AlbumSearch(albumInfo.Content, response);
        }

        var notFoundDesc = localizer.Translate("album.notFound");
        response.Embed.WithDescription(notFoundDesc);
        response.Embed.WithFooter(localizer.Translate("shared.searchValue", ("value", searchValue)));
        response.ComponentsContainer.WithTextDisplay(notFoundDesc);
        response.ComponentsContainer.WithTextDisplay($"-# {localizer.Translate("shared.searchValue", ("value", searchValue))}");
        response.CommandResponse = CommandResponse.NotFound;
        response.ResponseType = ResponseType.Embed;
        return new AlbumSearch(null, response);
    }

    private async Task<Album> SearchAlbumInDatabase(string searchQuery, int? userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.SearchAlbum(searchQuery, userId, connection);
    }

    private async Task<Response<AlbumInfo>> GetCachedAlbum(string artistName, string albumName, string lastFmUserName,
        int? userId = null,
        bool redirectsEnabled = true)
    {
        Response<AlbumInfo> albumInfo;
        var cachedAlbum = await GetAlbumFromDatabase(artistName, albumName, redirectsEnabled);
        if (cachedAlbum != null)
        {
            albumInfo = new Response<AlbumInfo>
            {
                Content = CachedAlbumToAlbumInfo(cachedAlbum),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount = await this._whoKnowsAlbumService.GetAlbumPlayCountForUser(cachedAlbum.Id,
                    userId.Value);
                if (userPlaycount == 0)
                {
                    albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(artistName, albumName,
                        lastFmUserName, redirectsEnabled);
                }
                else
                {
                    albumInfo.Content.UserPlaycount = userPlaycount;
                }
            }
        }
        else
        {
            albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(artistName, albumName,
                lastFmUserName, redirectsEnabled);
        }

        return albumInfo;
    }

    public async Task FillMissingAlbumCovers(IReadOnlyList<TopAlbum> topAlbums)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var albumsToUpdate = topAlbums
            .Where(a => string.IsNullOrWhiteSpace(a.AlbumCoverUrl))
            .ToList();

        if (albumsToUpdate.Any())
        {
            await AlbumRepository.GetAlbumCovers(albumsToUpdate, connection);
        }
    }

    public Task<Response<TopAlbumList>> FilterAlbumToReleaseYear(Response<TopAlbumList> albums, int year) =>
        this._albumFilterService.FilterAlbumToReleaseYear(albums, year);

    public Task<Response<TopAlbumList>> FilterAlbumToReleaseDecade(Response<TopAlbumList> albums, int decade) =>
        this._albumFilterService.FilterAlbumToReleaseDecade(albums, decade);

    public Task<Response<TopAlbumList>> FilterAlbumsThatAreSingles(Response<TopAlbumList> albums) =>
        this._albumFilterService.FilterAlbumsThatAreSingles(albums);

    public async Task<List<GuildAlbum>> FilterAlbumsToReleasePeriod(List<GuildAlbum> albums, DateTime periodStart,
        DateTime periodEnd)
    {
        if (albums.Count == 0)
        {
            return albums;
        }

        var lookup = await this._albumFilterService.GetAlbumEnrichmentLookup(
            albums.Select(s => s.ArtistName).ToArray(),
            albums.Select(s => s.AlbumName).ToArray());

        var newReleases = new List<GuildAlbum>();
        foreach (var album in albums)
        {
            if (!lookup.TryGetValue((album.AlbumName.ToLower(), album.ArtistName.ToLower()), out var row))
            {
                continue;
            }

            if (string.Equals(row.AlbumType, "single", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var releaseDate = ParseReleaseDate(row.ReleaseDate, row.ReleaseDatePrecision);
            if (releaseDate.HasValue && releaseDate.Value >= periodStart && releaseDate.Value < periodEnd)
            {
                newReleases.Add(album);
            }
        }

        return newReleases;
    }

    public async Task<List<GuildAlbum>> FilterGuildAlbumsThatAreSingles(List<GuildAlbum> albums)
    {
        if (albums.Count == 0)
        {
            return albums;
        }

        var lookup = await this._albumFilterService.GetAlbumEnrichmentLookup(
            albums.Select(s => s.ArtistName).ToArray(),
            albums.Select(s => s.AlbumName).ToArray());

        return albums
            .Where(w => !lookup.TryGetValue((w.AlbumName.ToLower(), w.ArtistName.ToLower()), out var row) ||
                        !string.Equals(row.AlbumType, "single", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static DateTime? ParseReleaseDate(string releaseDate, string precision) =>
        Core.AlbumFilterService.ParseReleaseDate(releaseDate, precision);

    public async Task<Album> GetAlbumForId(int albumId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Albums.FindAsync(albumId);
    }

    public async Task<Album> GetAlbumForSpotifyId(string spotifyId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Albums.FirstOrDefaultAsync(f => f.SpotifyId == spotifyId);
    }

    private async Task<string> ResolveAlbumFromLink(string input)
    {
        try
        {
            var linkResult = MusicLinkExtensions.TryParseMusicLink(input);
            if (linkResult == null)
            {
                return null;
            }

            switch (linkResult.Type)
            {
                case MusicLinkExtensions.MusicLinkType.SpotifyAlbum:
                {
                    var dbAlbum = await GetAlbumForSpotifyId(linkResult.Id);
                    if (dbAlbum != null)
                    {
                        return $"{dbAlbum.ArtistName} | {dbAlbum.Name}";
                    }

                    var spotifyAlbum = await this._spotifyService.GetAlbumById(linkResult.Id);
                    if (spotifyAlbum != null)
                    {
                        return $"{spotifyAlbum.Artists.First().Name} | {spotifyAlbum.Name}";
                    }

                    break;
                }
                case MusicLinkExtensions.MusicLinkType.AppleMusicAlbum:
                {
                    if (long.TryParse(linkResult.Id, out var appleMusicId))
                    {
                        var dbAlbum = await this._appleMusicService.GetAlbumForAppleMusicId(appleMusicId);
                        if (dbAlbum != null)
                        {
                            return $"{dbAlbum.ArtistName} | {dbAlbum.Name}";
                        }
                    }

                    var appleMusicAlbum = await this._appleMusicService.GetAppleMusicAlbumById(linkResult.Id);
                    if (appleMusicAlbum?.Attributes != null)
                    {
                        return $"{appleMusicAlbum.Attributes.ArtistName} | {appleMusicAlbum.Attributes.Name}";
                    }

                    break;
                }
                case MusicLinkExtensions.MusicLinkType.SpotifyTrack:
                {
                    await using var db = await this._contextFactory.CreateDbContextAsync();
                    var dbTrack = await db.Tracks.FirstOrDefaultAsync(f => f.SpotifyId == linkResult.Id);
                    if (dbTrack is { ArtistName: not null, AlbumName: not null })
                    {
                        return $"{dbTrack.ArtistName} | {dbTrack.AlbumName}";
                    }

                    var spotifyTrack = await this._spotifyService.GetTrackById(linkResult.Id);
                    if (spotifyTrack?.Album != null)
                    {
                        return $"{spotifyTrack.Artists.First().Name} | {spotifyTrack.Album.Name}";
                    }

                    break;
                }
                case MusicLinkExtensions.MusicLinkType.AppleMusicSong:
                {
                    if (long.TryParse(linkResult.Id, out var appleMusicId))
                    {
                        var dbTrack = await this._appleMusicService.GetTrackForAppleMusicId(appleMusicId);
                        if (dbTrack?.ArtistName != null && dbTrack.AlbumName != null)
                        {
                            return $"{dbTrack.ArtistName} | {dbTrack.AlbumName}";
                        }
                    }

                    var appleMusicSong = await this._appleMusicService.GetAppleMusicSongById(linkResult.Id);
                    if (appleMusicSong?.Attributes?.AlbumName != null)
                    {
                        return $"{appleMusicSong.Attributes.ArtistName} | {appleMusicSong.Attributes.AlbumName}";
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to resolve album from link: {input}", input);
        }

        return null;
    }

    public async Task<Album> GetAlbumFromDatabase(string artistName, string albumName, bool redirectsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumName))
        {
            return null;
        }

        var alias = await this._aliasService.GetAlias(artistName);

        var correctedArtistName = artistName;
        if (alias != null && !alias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls) && redirectsEnabled)
        {
            correctedArtistName = alias.ArtistName;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var album = await AlbumRepository.GetAlbumForName(correctedArtistName, albumName, connection);

        await connection.CloseAsync();

        return album;
    }

    private async Task<string> GetAlbumColorAsync(int albumId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.GetAlbumBackgroundColor(albumId, connection);
    }

    public async Task<Color> GetAlbumAccentColor(string albumCoverUrl, string albumName, string artistName,
        Album prefetchedAlbum = null)
    {
        if (string.IsNullOrEmpty(albumName) || string.IsNullOrEmpty(artistName))
        {
            return DiscordConstants.LastFmColorRed;
        }

        var accentCacheKey = $"album-accent-{artistName.ToUpperInvariant()}\0{albumName.ToUpperInvariant()}\0{albumCoverUrl}";
        if (this._cache.TryGetValue(accentCacheKey, out Color cachedAccentColor))
        {
            return cachedAccentColor;
        }

        var color = await GetAlbumAccentColorInternal(albumCoverUrl, albumName, artistName, prefetchedAlbum);
        this._cache.Set(accentCacheKey, color,
            color.Equals(DiscordConstants.LastFmColorRed) ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(1));
        return color;
    }

    private async Task<Color> GetAlbumAccentColorInternal(string albumCoverUrl, string albumName, string artistName,
        Album prefetchedAlbum)
    {
        var album = prefetchedAlbum ?? await GetAlbumFromDatabase(artistName, albumName);

        var cachePath = ChartService.AlbumUrlToCacheFilePath(albumName, artistName);
        if (File.Exists(cachePath))
        {
            using var bitmap = SKBitmap.Decode(cachePath);
            if (bitmap != null)
            {
                var accentColor = bitmap.GetAccentColor();
                return new Color(accentColor.R, accentColor.G, accentColor.B);
            }
        }

        if (album != null)
        {
            var colorHex = await GetAlbumColorAsync(album.Id);
            if (!string.IsNullOrEmpty(colorHex) &&
                int.TryParse(colorHex, NumberStyles.HexNumber, null, out var rgb))
            {
                return new Color(rgb);
            }
        }

        if (string.IsNullOrEmpty(albumCoverUrl))
        {
            return DiscordConstants.LastFmColorRed;
        }

        try
        {
            var processedUrl = albumCoverUrl;
            if (processedUrl.Contains("lastfm.freetls.fastly.net"))
            {
                processedUrl = processedUrl.Replace("/770x0/", "/").Replace("/300x300/", "/");
            }

            await using var imageStream = await this._dataSourceFactory.GetAlbumImageAsStreamAsync(processedUrl);
            if (imageStream != null)
            {
                var cacheStream = new MemoryStream();
                await imageStream.CopyToAsync(cacheStream);
                imageStream.Position = 0;

                using var bitmap = SKBitmap.Decode(imageStream);
                if (bitmap != null)
                {
                    cacheStream.Position = 0;
                    await ChartService.OverwriteCache(cacheStream, cachePath);
                    await cacheStream.DisposeAsync();

                    var accentColor = bitmap.GetAccentColor();
                    return new Color(accentColor.R, accentColor.G, accentColor.B);
                }

                await cacheStream.DisposeAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while downloading album cover for accent color");
        }

        return DiscordConstants.LastFmColorRed;
    }

    public async Task<Color> GetAccentColorWithAlbum(ContextModel context, string albumCoverUrl, int? albumId, string albumName, string artistName, bool allowCustomColors = true, Album prefetchedAlbum = null)
    {
        var accentColor = context.ContextUser?.FmSetting?.AccentColor;
        if (!allowCustomColors && accentColor is FmAccentColor.Custom or FmAccentColor.LastFmRed)
        {
            accentColor = FmAccentColor.CoverColor;
        }

        switch (accentColor)
        {
            case FmAccentColor.LastFmRed:
                return DiscordConstants.LastFmColorRed;
            case FmAccentColor.RoleColor:
            case FmAccentColor.Custom:
                return await UserService.GetAccentColor(context.ContextUser, context.DiscordGuild);
            case FmAccentColor.CoverColor:
            case FmAccentColor.AppleMusicBackgroundColor:
            case null:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (accentColor == FmAccentColor.AppleMusicBackgroundColor && albumId.HasValue)
        {
            var bgColor = await GetAlbumColorAsync(albumId.Value);
            if (!string.IsNullOrEmpty(bgColor) &&
                int.TryParse(bgColor, NumberStyles.HexNumber, null, out var bgRgb))
            {
                return new Color(bgRgb);
            }
        }

        return await GetAlbumAccentColor(albumCoverUrl, albumName, artistName, prefetchedAlbum);
    }

    private static AlbumInfo CachedAlbumToAlbumInfo(Album album)
    {
        return new AlbumInfo
        {
            AlbumCoverUrl = album.SpotifyImageUrl ?? album.LastfmImageUrl,
            AlbumName = album.Name,
            ArtistName = album.ArtistName,
            ArtistUrl = LastfmUrlExtensions.GetArtistUrl(album.ArtistName),
            Mbid = album.Mbid,
            AlbumUrl = album.LastFmUrl
        };
    }

    public async Task<List<TopAlbum>> GetUserAllTimeTopAlbums(int userId, bool useCache = false)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var cacheKey = $"user-{userId}-topalbums-alltime";
        if (this._cache.TryGetValue(cacheKey, out List<TopAlbum> topAlbums) && useCache)
        {
            return topAlbums;
        }

        var freshTopAlbums = (await AlbumRepository.GetUserAlbums(userId, connection))
            .Select(s => new TopAlbum()
            {
                ArtistName = s.ArtistName,
                AlbumName = s.Name,
                UserPlaycount = s.Playcount,
                ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.ArtistName),
                AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(s.ArtistName, s.Name),
            })
            .OrderByDescending(o => o.UserPlaycount)
            .ToList();

        if (freshTopAlbums.Count > 100)
        {
            this._cache.Set(cacheKey, freshTopAlbums, TimeSpan.FromMinutes(10));
        }

        return freshTopAlbums;
    }

    public Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleaseYear(int userId, int year) =>
        this._albumFilterService.GetUserAllTimeTopAlbumsByReleaseYear(userId, year);

    public Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleaseDecade(int userId, int decade) =>
        this._albumFilterService.GetUserAllTimeTopAlbumsByReleaseDecade(userId, decade);

    public async Task<List<AlbumPopularity>> GetUserAllTimeTopAlbumsPopularity(int userId, List<TopAlbum> topAlbums)
    {
        var cacheKey = $"user-{userId}-topalbums-alltime-popularity";
        if (this._cache.TryGetValue(cacheKey, out List<AlbumPopularity> cachedPopularity))
        {
            return cachedPopularity;
        }

        var popularity = await GetAlbumsPopularity(topAlbums);

        if (topAlbums.Count > 100)
        {
            this._cache.Set(cacheKey, popularity, TimeSpan.FromMinutes(10));
        }

        return popularity;
    }

    public async Task<List<AlbumPopularity>> GetAlbumsPopularity(List<TopAlbum> topAlbums)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var albumsWithPopularity = await AlbumRepository.GetAlbumsPopularity(topAlbums, connection);

        var albumLookup = topAlbums
            .GroupBy(g => (g.ArtistName.ToLowerInvariant(), g.AlbumName.ToLowerInvariant()))
            .ToDictionary(
                d => d.Key,
                d => d.OrderByDescending(o => o.UserPlaycount).First().UserPlaycount ?? 0
            );

        foreach (var album in albumsWithPopularity)
        {
            var key = (album.ArtistName.ToLowerInvariant(), album.Name.ToLowerInvariant());
            if (albumLookup.TryGetValue(key, out var playcount))
            {
                album.Playcount = playcount;
            }
        }

        return albumsWithPopularity;
    }

    public async Task<List<AlbumAutoCompleteSearchModel>> GetLatestAlbums(ulong discordUserId, bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-albums-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<AlbumAutoCompleteSearchModel> userArtists);
            if (cacheAvailable && cacheEnabled)
            {
                return userArtists;
            }

            var user = await this._userService.GetUserAsync(discordUserId);

            if (user == null)
            {
                return [new AlbumAutoCompleteSearchModel(Constants.AutoCompleteLoginRequired)];
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection,
                DateTime.UtcNow.AddDays(-2));

            var albums = plays
                .Where(w => w.AlbumName != null)
                .OrderByDescending(o => o.TimePlayed)
                .Select(s => new AlbumAutoCompleteSearchModel(s.ArtistName, s.AlbumName))
                .Distinct()
                .ToList();

            this._cache.Set(cacheKey, albums, TimeSpan.FromSeconds(30));

            return albums;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in {method}", nameof(GetLatestAlbums));
            throw;
        }
    }

    public async Task<List<AlbumAutoCompleteSearchModel>> GetRecentTopAlbums(ulong discordUserId,
        bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-top-albums-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<AlbumAutoCompleteSearchModel> userAlbums);
            if (cacheAvailable && cacheEnabled)
            {
                return userAlbums;
            }

            var user = await this._userService.GetUserAsync(discordUserId);

            if (user == null)
            {
                return [new AlbumAutoCompleteSearchModel(Constants.AutoCompleteLoginRequired)];
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection,
                DateTime.UtcNow.AddDays(-20));

            var albums = plays
                .Where(w => w.AlbumName != null)
                .GroupBy(g => new AlbumAutoCompleteSearchModel(g.ArtistName, g.AlbumName))
                .OrderByDescending(o => o.Count())
                .Select(s => s.Key)
                .ToList();

            this._cache.Set(cacheKey, albums, TimeSpan.FromSeconds(120));

            return albums;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in {method}", nameof(GetRecentTopAlbums));
            throw;
        }
    }

    public async Task<List<AlbumAutoCompleteSearchModel>> SearchThroughAlbums(string searchValue)
    {
        try
        {
            var reply = await this._albumEnrichment.SearchAlbumsAsync(new AlbumSearchRequest
            {
                SearchValue = searchValue ?? string.Empty
            });

            return reply.Albums
                .Select(s => new AlbumAutoCompleteSearchModel(s.ArtistName, s.Name, s.Popularity))
                .ToList();
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in {method}", nameof(SearchThroughAlbums));
            return [];
        }
    }

    public async Task<List<AlbumImage>> GetAlbumImages(int albumId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.AlbumImages
            .Where(w => w.AlbumId == albumId)
            .ToListAsync();
    }

    public static string GetAlbumReleaseDate(Album album)
    {
        if (album.ReleaseDate == null)
        {
            return null;
        }

        switch (album.ReleaseDatePrecision)
        {
            case null:
            case "year":
                return $"`{album.ReleaseDate}`";
        }

        var parsedDateTime = album.ReleaseDatePrecision switch
        {
            "year" => DateTime.Parse($"{album.ReleaseDate}-1-1"),
            "month" => DateTime.Parse($"{album.ReleaseDate}-1"),
            "day" => DateTime.Parse(album.ReleaseDate),
            _ => throw new NotImplementedException()
        };

        if (album.ReleaseDatePrecision == "month")
        {
            return parsedDateTime.ToString("MMMM yyyy");
        }

        var specifiedDateTime = DateTime.SpecifyKind(parsedDateTime.AddHours(12), DateTimeKind.Utc);
        var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

        return $"<t:{dateValue}:D>";
    }
}
