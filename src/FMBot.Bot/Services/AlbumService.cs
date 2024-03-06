using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
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
            string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null, ulong? interactionId = null, IUserMessage referencedMessage = null)
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
                    albumInfo = await GetCachedAlbum(searchArtistName, searchAlbumName, lastFmUserName, userId);
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
                PublicProperties.UsedCommandsAlbums.TryAdd(interactionId.Value, albumInfo.Content.AlbumName);
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

    private async Task<Response<AlbumInfo>> GetCachedAlbum(string artistName, string albumName, string lastFmUserName, int? userId = null)
    {
        Response<AlbumInfo> albumInfo;
        var cachedAlbum = await GetAlbumFromDatabase(artistName, albumName);
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
                lastFmUserName);
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

        foreach (var topAlbum in topAlbums.Where(w => w.AlbumCoverUrl == null))
        {
            var album = albums.Albums.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.AlbumCoverUrl) &&
                                                        f.AlbumName == topAlbum.AlbumName &&
                                                        f.ArtistName == topAlbum.ArtistName);

            if (album != null)
            {
                topAlbum.AlbumCoverUrl = album.AlbumCoverUrl;
            }
        }

        return topAlbums;
    }

    public async Task<Album> GetAlbumForId(int albumId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Albums.FindAsync(albumId);
    }

    public async Task<Album> GetAlbumFromDatabase(string artistName, string albumName)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumName))
        {
            return null;
        }

        var alias = await this._aliasService.GetAlias(artistName);

        var correctedArtistName = artistName;
        if (alias != null && !alias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls))
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
                UserPlaycount = s.Playcount
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
