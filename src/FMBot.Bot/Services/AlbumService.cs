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
    private readonly TimerService _timer;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly UpdateService _updateService;
    private readonly AliasService _aliasService;
    private readonly UserService _userService;
    private readonly AlbumEnrichment.AlbumEnrichmentClient _albumEnrichment;

    public AlbumService(IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory,
        TimerService timer,
        WhoKnowsAlbumService whoKnowsAlbumService,
        IDbContextFactory<FMBotDbContext> contextFactory,
        UpdateService updateService,
        AliasService aliasService,
        UserService userService,
        AlbumEnrichment.AlbumEnrichmentClient albumEnrichment)
    {
        this._cache = cache;
        this._dataSourceFactory = dataSourceFactory;
        this._timer = timer;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._contextFactory = contextFactory;
        this._updateService = updateService;
        this._aliasService = aliasService;
        this._userService = userService;
        this._albumEnrichment = albumEnrichment;
        this._botSettings = botSettings.Value;
    }

    public async Task<AlbumSearch> SearchAlbum(ResponseModel response, NetCord.User discordUser, string albumValues,
        string lastFmUserName, string sessionKey = null,
        string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null, ulong? interactionId = null,
        RestMessage referencedMessage = null, bool redirectsEnabled = true)
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
                searchValue = $"{this._timer.CurrentFeatured.ArtistName} | {this._timer.CurrentFeatured.AlbumName}";
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
                    response.Embed.WithDescription(
                        $"Album `{searchAlbumName}` by `{searchArtistName}` could not be found, please check your search values and try again.");
                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return new AlbumSearch(null, response);
                }

                if (!albumInfo.Success || albumInfo.Content == null)
                {
                    response.Embed.ErrorResponse(albumInfo.Error, albumInfo.Message, null, discordUser, "album");
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
                recentScrobbles = await this._updateService.UpdateUser(new UpdateUserQueueItem(userId.Value));
            }
            else
            {
                recentScrobbles =
                    await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                var errorResponse =
                    GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, lastFmUserName);
                return new AlbumSearch(null, errorResponse);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            if (string.IsNullOrWhiteSpace(lastPlayedTrack.AlbumName))
            {
                response.Embed.WithDescription(
                    $"The track you're scrobbling (**{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**) does not have an album associated with it according to Last.fm.\n" +
                    $"Please note that .fmbot is not associated with Last.fm.");

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
                response.Embed.WithDescription(
                    $"Last.fm did not return a result for **{lastPlayedTrack.AlbumName}** by **{lastPlayedTrack.ArtistName}**.\n" +
                    $"This usually happens on recently released albums or on albums by smaller artists. Please try again later.\n\n" +
                    $"Please note that .fmbot is not associated with Last.fm.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            return new AlbumSearch(albumInfo.Content, response);
        }

        var albumSearch = await this.SearchAlbumInDatabase(searchValue);
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
                response.Embed.ErrorResponse(albumInfo.Error, albumInfo.Message, null, discordUser, "album");
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            return new AlbumSearch(albumInfo.Content, response);
        }

        response.Embed.WithDescription($"Album could not be found, please check your search values and try again.\n\n" +
                                       $"You can also enter the exact value with the | separator. Example: `artist name | album name`");
        response.Embed.WithFooter($"Search value: '{searchValue}'");
        response.CommandResponse = CommandResponse.NotFound;
        response.ResponseType = ResponseType.Embed;
        return new AlbumSearch(null, response);
    }

    private async Task<Album> SearchAlbumInDatabase(string searchQuery)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.SearchAlbum(searchQuery, connection);
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
                var userPlaycount = await this._whoKnowsAlbumService.GetAlbumPlayCountForUser(cachedAlbum.ArtistName,
                    cachedAlbum.Name, userId.Value);
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

    public async Task<List<TopAlbum>> FillMissingAlbumCovers(List<TopAlbum> topAlbums)
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

        return topAlbums;
    }

    public async Task<Response<TopAlbumList>> FilterAlbumToReleaseYear(Response<TopAlbumList> albums, int year)
    {
        await EnrichTopAlbums(albums.Content.TopAlbums);

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = yearStart.AddYears(1).AddSeconds(-1);
        albums.Content.TopAlbums = albums.Content.TopAlbums
            .Where(w => w.ReleaseDate.HasValue &&
                        w.ReleaseDate.Value >= yearStart &&
                        w.ReleaseDate.Value <= yearEnd)
            .ToList();

        DataSourceFactory.AddAlbumTopList(albums, null);

        return albums;
    }

    public async Task<Response<TopAlbumList>> FilterAlbumToReleaseDecade(Response<TopAlbumList> albums, int decade)
    {
        await EnrichTopAlbums(albums.Content.TopAlbums);

        var decadeStart = new DateTime(decade, 1, 1);
        var decadeEnd = decadeStart.AddYears(10).AddSeconds(-1);
        albums.Content.TopAlbums = albums.Content.TopAlbums
            .Where(w => w.ReleaseDate.HasValue &&
                        w.ReleaseDate.Value >= decadeStart &&
                        w.ReleaseDate.Value <= decadeEnd)
            .ToList();

        DataSourceFactory.AddAlbumTopList(albums, null);

        return albums;
    }

    private async Task EnrichTopAlbums(IReadOnlyCollection<TopAlbum> list)
    {
        var minTimestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        var request = new AlbumReleaseDateRequest
        {
            Albums =
            {
                list.Select(s => new AlbumWithDate
                {
                    ArtistName = s.ArtistName,
                    AlbumName = s.AlbumName,
                    ReleaseDate = minTimestamp,
                    ReleaseDatePrecision = s.ReleaseDatePrecision ?? ""
                })
            }
        };

        var albums = await this._albumEnrichment.AddAlbumReleaseDatesAsync(request);

        var albumGroups = albums.Albums
            .Where(a => a.ReleaseDate != minTimestamp)
            .GroupBy(a => (a.AlbumName.ToLower(), a.ArtistName.ToLower()))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var topAlbum in list.Where(w => w.ReleaseDate == null))
        {
            var key = (topAlbum.AlbumName.ToLower(), topAlbum.ArtistName.ToLower());

            if (albumGroups.TryGetValue(key, out var matchedAlbums))
            {
                var album = matchedAlbums.FirstOrDefault();
                if (album != null)
                {
                    topAlbum.ReleaseDate = album.ReleaseDate.ToDateTime();
                    topAlbum.ReleaseDatePrecision = album.ReleaseDatePrecision;
                }
            }
        }
    }

    public async Task<Album> GetAlbumForId(int albumId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Albums.FindAsync(albumId);
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

        const string sql = @"
            SELECT bg_color FROM album_images
            WHERE album_id = @albumId AND bg_color IS NOT NULL
            LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<string>(sql, new { albumId });
    }

    public async Task<Color> GetAlbumAccentColorAsync(string albumCoverUrl, int? albumId, string albumName, string artistName)
    {
        if (string.IsNullOrEmpty(albumName) || string.IsNullOrEmpty(artistName))
        {
            return DiscordConstants.LastFmColorRed;
        }

        var cachePath = ChartService.AlbumUrlToCacheFilePath(albumName, artistName);
        if (File.Exists(cachePath))
        {
            using var bitmap = SKBitmap.Decode(cachePath);
            if (bitmap != null)
            {
                var avgColor = bitmap.GetAverageRgbColor();
                return new Color(avgColor.R, avgColor.G, avgColor.B);
            }
        }

        if (albumId.HasValue)
        {
            var colorHex = await GetAlbumColorAsync(albumId.Value);
            if (!string.IsNullOrEmpty(colorHex) &&
                int.TryParse(colorHex, NumberStyles.HexNumber, null, out var rgb))
            {
                return new Color(rgb);
            }
        }

        if (!string.IsNullOrEmpty(albumCoverUrl))
        {
            try
            {
                var processedUrl = albumCoverUrl;
                if (processedUrl.Contains("lastfm.freetls.fastly.net") &&
                    !processedUrl.Contains("/300x300/"))
                {
                    processedUrl = processedUrl.Replace("/770x0/", "/");
                    processedUrl = processedUrl.Replace("/i/u/", "/i/u/300x300/");
                }

                var imageStream = await this._dataSourceFactory.GetAlbumImageAsStreamAsync(processedUrl);
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

                        var avgColor = bitmap.GetAverageRgbColor();
                        return new Color(avgColor.R, avgColor.G, avgColor.B);
                    }

                    await cacheStream.DisposeAsync();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while downloading album cover for accent color");
            }
        }

        // Default
        return DiscordConstants.LastFmColorRed;
    }

    public AlbumInfo CachedAlbumToAlbumInfo(Album album)
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

    public async Task<List<AlbumAutoCompleteSearchModel>> SearchThroughAlbums(string searchValue,
        bool cacheEnabled = true)
    {
        try
        {
            const string cacheKey = "albums-all";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<AlbumAutoCompleteSearchModel> albums);
            if (!cacheAvailable && cacheEnabled)
            {
                const string sql = "SELECT name, artist_name, popularity " +
                                   "FROM public.albums " +
                                   "WHERE popularity IS NOT NULL AND name IS NOT NULL and artist_name IS NOT NULL AND popularity > 5 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var albumQuery = (await connection.QueryAsync<Album>(sql)).ToList();

                albums = albumQuery
                    .Select(s => new AlbumAutoCompleteSearchModel(s.ArtistName, s.Name, s.Popularity))
                    .ToList();

                this._cache.Set(cacheKey, albums, TimeSpan.FromHours(2));
            }

            var results = albums.Where(w =>
                    w.Name.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    w.Artist.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    w.Artist.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.Popularity)
                .ToList();

            return results;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in {method}", nameof(SearchThroughAlbums));
            throw;
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
