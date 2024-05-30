using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
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
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
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
    private readonly IUpdateService _updateService;
    private readonly AliasService _aliasService;
    private readonly UserService _userService;
    private readonly AlbumEnrichment.AlbumEnrichmentClient _albumEnrichment;

    public AlbumService(IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory,
        TimerService timer,
        WhoKnowsAlbumService whoKnowsAlbumService,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IUpdateService updateService,
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

    public async Task<AlbumSearch> SearchAlbum(ResponseModel response, IUser discordUser, string albumValues, string lastFmUserName, string sessionKey = null,
            string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null, ulong? interactionId = null, IUserMessage referencedMessage = null, bool redirectsEnabled = true)
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
                    albumInfo = await GetCachedAlbum(searchArtistName, searchAlbumName, lastFmUserName, userId, redirectsEnabled);
                }
                else
                {
                    albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(searchArtistName, searchAlbumName,
                        lastFmUserName);
                }

                if (interactionId.HasValue)
                {
                    PublicProperties.UsedCommandsArtists.TryAdd(interactionId.Value, searchArtistName);
                    PublicProperties.UsedCommandsAlbums.TryAdd(interactionId.Value, searchAlbumName);
                    response.ReferencedMusic = new ReferencedMusic
                    {
                        Artist = searchArtistName,
                        Album = searchAlbumName
                    };
                }

                if (!albumInfo.Success && albumInfo.Error == ResponseStatus.MissingParameters)
                {
                    response.Embed.WithDescription($"Album `{searchAlbumName}` by `{searchArtistName}` could not be found, please check your search values and try again.");
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
                recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                var errorResponse = GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, lastFmUserName);
                return new AlbumSearch(null, errorResponse);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            if (string.IsNullOrWhiteSpace(lastPlayedTrack.AlbumName))
            {
                response.Embed.WithDescription($"The track you're scrobbling (**{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**) does not have an album associated with it according to Last.fm.\n" +
                                            $"Please note that .fmbot is not associated with Last.fm.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName, lastFmUserName, userId);
            }
            else
            {
                albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName,
                    lastFmUserName);
            }

            if (interactionId.HasValue)
            {
                PublicProperties.UsedCommandsArtists.TryAdd(interactionId.Value, lastPlayedTrack.ArtistName);
                PublicProperties.UsedCommandsAlbums.TryAdd(interactionId.Value, lastPlayedTrack.AlbumName);
                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = lastPlayedTrack.ArtistName,
                    Album = lastPlayedTrack.AlbumName
                };
            }

            if (albumInfo?.Content == null || !albumInfo.Success)
            {
                response.Embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.AlbumName}** by **{lastPlayedTrack.ArtistName}**.\n" +
                                            $"This usually happens on recently released albums or on albums by smaller artists. Please try again later.\n\n" +
                                            $"Please note that .fmbot is not associated with Last.fm.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            return new AlbumSearch(albumInfo.Content, response);
        }

        var result = await this._dataSourceFactory.SearchAlbumAsync(searchValue);
        if (result.Success && result.Content != null)
        {
            var album = result.Content;

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(album.ArtistName, album.AlbumName, lastFmUserName, userId);
            }
            else
            {
                albumInfo = await this._dataSourceFactory.GetAlbumInfoAsync(album.ArtistName, album.AlbumName,
                    lastFmUserName);
            }

            if (interactionId.HasValue)
            {
                PublicProperties.UsedCommandsArtists.TryAdd(interactionId.Value, albumInfo.Content.ArtistName);
                if (albumInfo.Content.AlbumName != null)
                {
                    PublicProperties.UsedCommandsAlbums.TryAdd(interactionId.Value, albumInfo.Content.AlbumName);
                }

                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = albumInfo.Content.ArtistName,
                    Album = albumInfo.Content.AlbumName
                };
            }

            return new AlbumSearch(albumInfo.Content, response);
        }

        if (result.Success)
        {
            response.Embed.WithDescription($"Album could not be found, please check your search values and try again.");
            response.Embed.WithFooter($"Search value: '{searchValue}'");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return new AlbumSearch(null, response);
        }

        response.Embed.WithDescription($"Last.fm returned an error: {result.Error}");
        response.CommandResponse = CommandResponse.LastFmError;
        response.ResponseType = ResponseType.Embed;
        return new AlbumSearch(null, response);
    }

    private async Task<Response<AlbumInfo>> GetCachedAlbum(string artistName, string albumName, string lastFmUserName, int? userId = null,
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
                albumInfo.Content.UserPlaycount = userPlaycount;
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
        var request = new AlbumRequest
        {
            Albums =
            {
                topAlbums.Select(s => new AlbumWithCover
                {
                    ArtistName = s.ArtistName,
                    AlbumName = s.AlbumName,
                    AlbumCoverUrl = s.AlbumCoverUrl ?? ""
                })
            }
        };

        var albums = await this._albumEnrichment.AddMissingAlbumCoversAsync(request);

        var albumDict = albums.Albums
            .Where(a => !string.IsNullOrWhiteSpace(a.AlbumCoverUrl))
            .GroupBy(a => (a.AlbumName.ToLower(), a.ArtistName.ToLower()))
            .ToDictionary(
                g => g.Key,
                g => g.First(f => f.AlbumCoverUrl != null)
            );

        foreach (var topAlbum in topAlbums)
        {
            if (topAlbum.AlbumCoverUrl == null)
            {
                var key = (topAlbum.AlbumName.ToLower(), topAlbum.ArtistName.ToLower());
                if (albumDict.TryGetValue(key, out var coverUrl))
                {
                    topAlbum.AlbumCoverUrl = coverUrl.AlbumCoverUrl;
                }
            }
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
        var minTimestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
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

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<AlbumAutoCompleteSearchModel> { new(Constants.AutoCompleteLoginRequired) };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-2));

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
            Log.Error($"Error in {nameof(GetLatestAlbums)}", e);
            throw;
        }
    }

    public async Task<List<AlbumAutoCompleteSearchModel>> GetRecentTopAlbums(ulong discordUserId, bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-top-albums-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<AlbumAutoCompleteSearchModel> userAlbums);
            if (cacheAvailable && cacheEnabled)
            {
                return userAlbums;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<AlbumAutoCompleteSearchModel> { new(Constants.AutoCompleteLoginRequired) };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-20));

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
            Log.Error($"Error in {nameof(GetRecentTopAlbums)}", e);
            throw;
        }
    }

    public async Task<List<AlbumAutoCompleteSearchModel>> SearchThroughAlbums(string searchValue, bool cacheEnabled = true)
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
            Log.Error($"Error in {nameof(SearchThroughAlbums)}", e);
            throw;
        }
    }
}
