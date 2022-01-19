using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
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

        public AlbumService(IMemoryCache cache, IOptions<BotSettings> botSettings, AlbumRepository albumRepository)
        {
            this._cache = cache;
            this._albumRepository = albumRepository;
            this._botSettings = botSettings.Value;
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

        public AlbumInfo CachedAlbumToAlbumInfo (Album album)
        {
            return new AlbumInfo
            {
                AlbumCoverUrl = album.SpotifyImageUrl ?? album.LastfmImageUrl,
                AlbumName = album.Name,
                ArtistName = album.ArtistName,
                Mbid = album.Mbid
            };
        }
    }
}
