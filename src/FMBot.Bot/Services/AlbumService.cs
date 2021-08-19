using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services
{
    public class AlbumService
    {
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;

        public AlbumService(IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public async Task<List<TopAlbum>> FillMissingAlbumCovers(List<TopAlbum> topAlbums)
        {
            if (topAlbums.All(a => a.AlbumCoverUrl != null))
            {
                return topAlbums;
            }

            await CacheAllSpotifyAlbumCovers();

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

        private async Task CacheAllSpotifyAlbumCovers()
        {
            const string cacheKey = "album-spotify-covers";
            var cacheTime = TimeSpan.FromMinutes(5);

            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            const string sql = "SELECT LOWER(last_fm_url) as last_fm_url, LOWER(spotify_image_url) as spotify_image_url " +
                               "FROM public.albums where last_fm_url is not null and spotify_image_url is not null;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var albumCovers = (await connection.QueryAsync<AlbumSpotifyCoverDto>(sql)).ToList();

            foreach (var cover in albumCovers)
            {
                this._cache.Set(CacheKeyForAlbumCover(cover.LastFmUrl), cover.SpotifyImageUrl, cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        private static string CacheKeyForAlbumCover(string lastFmUrl)
        {
            return $"album-spotify-cover-{lastFmUrl}";
        }
    }
}
