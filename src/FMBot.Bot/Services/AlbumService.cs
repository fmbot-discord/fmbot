using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services
{
    public class AlbumService
    {
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;
        private readonly AlbumRepository _albumRepository;
        private readonly LastFmRepository _lastFmRepository;
        private readonly TimerService _timer;
        private readonly WhoKnowsAlbumService _whoKnowsAlbumService;

        public AlbumService(IMemoryCache cache, IOptions<BotSettings> botSettings, AlbumRepository albumRepository, LastFmRepository lastFmRepository, TimerService timer, WhoKnowsAlbumService whoKnowsAlbumService)
        {
            this._cache = cache;
            this._albumRepository = albumRepository;
            this._lastFmRepository = lastFmRepository;
            this._timer = timer;
            this._whoKnowsAlbumService = whoKnowsAlbumService;
            this._botSettings = botSettings.Value;
        }

        public async Task<AlbumSearch> SearchAlbum(ResponseModel response, IUser discordUser, string albumValues, string lastFmUserName, string sessionKey = null,
                string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null)
        {
            string searchValue;
            if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.Length != 0)
            {
                searchValue = albumValues;

                if (searchValue.ToLower() == "featured")
                {
                    searchValue = $"{this._timer._currentFeatured.ArtistName} | {this._timer._currentFeatured.AlbumName}";
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
                        albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(searchArtistName, searchAlbumName,
                            lastFmUserName);
                    }

                    if (!albumInfo.Success && albumInfo.Error == ResponseStatus.MissingParameters)
                    {
                        response.Embed.WithDescription($"Album `{searchAlbumName}` by `{searchArtistName}`could not be found, please check your search values and try again.");
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
                var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);

                if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
                {
                    response.Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(recentScrobbles, lastFmUserName);
                    response.ResponseType = ResponseType.Embed;
                    return new AlbumSearch(null, response);
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
                    albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName,
                        lastFmUserName);
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

            var result = await this._lastFmRepository.SearchAlbumAsync(searchValue);
            if (result.Success && result.Content.Any())
            {
                var album = result.Content[0];

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                Response<AlbumInfo> albumInfo;
                if (useCachedAlbums)
                {
                    albumInfo = await GetCachedAlbum(album.ArtistName, album.Name, lastFmUserName, userId);
                }
                else
                {
                    albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(album.ArtistName, album.Name,
                        lastFmUserName);
                }

                return new AlbumSearch(albumInfo.Content, response);
            }

            if (result.Success)
            {
                response.Embed.WithDescription($"Album could not be found, please check your search values and try again.");
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
                return new AlbumSearch(null, response);
            }

            response.Embed.WithDescription($"Last.fm returned an error: {result.Status}");
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
                albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(artistName, albumName,
                    lastFmUserName);
            }

            return albumInfo;
        }

        public async Task<List<TopAlbum>> FillMissingAlbumCovers(List<TopAlbum> topAlbums)
        {
            if (topAlbums.All(a => a.AlbumCoverUrl != null))
            {
                return topAlbums;
            }

            await CacheAllAlbumCovers();

            foreach (var topAlbum in topAlbums.Where(w => w.AlbumCoverUrl == null))
            {
                var url = topAlbum.AlbumUrl.ToLower();
                var albumCover = (string)this._cache.Get(CacheKeyForAlbumCover(url));

                if (albumCover != null)
                {
                    topAlbum.AlbumCoverUrl = albumCover;
                }
            }

            return topAlbums;
        }

        private async Task CacheAllAlbumCovers()
        {
            const string cacheKey = "album-covers";
            var cacheTime = TimeSpan.FromMinutes(5);

            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            const string sql = "SELECT LOWER(last_fm_url) as last_fm_url, LOWER(spotify_image_url) as spotify_image_url, LOWER(lastfm_image_url) as lastfm_image_url " +
                               "FROM public.albums where last_fm_url is not null and (spotify_image_url is not null or lastfm_image_url is not null);";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var albumCovers = (await connection.QueryAsync<AlbumCoverDto>(sql)).ToList();

            foreach (var cover in albumCovers)
            {
                this._cache.Set(CacheKeyForAlbumCover(cover.LastFmUrl), cover.LastfmImageUrl ?? cover.SpotifyImageUrl, cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        public static string CacheKeyForAlbumCover(string lastFmUrl)
        {
            return $"album-spotify-cover-{lastFmUrl.ToLower()}";
        }

        public async Task<Album> GetAlbumFromDatabase(string artistName, string albumName)
        {
            if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumName))
            {
                return null;
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var album = await this._albumRepository.GetAlbumForName(artistName, albumName, connection);

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

            var freshTopAlbums = (await this._albumRepository.GetUserAlbums(userId, connection))
                .Select(s => new TopAlbum()
                {
                    ArtistName = s.Name,
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
    }
}
