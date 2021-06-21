using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Configurations;
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

            var albumCovers = await GetCachedAlbumCovers();

            foreach (var topAlbum in topAlbums.Where(w => w.AlbumCoverUrl == null))
            {
                var url = topAlbum.AlbumUrl.ToLower();
                var albumCover = albumCovers.FirstOrDefault(item => item.LastFmUrl.Contains(url));

                if (albumCover != null)
                {
                    topAlbum.AlbumCoverUrl = albumCover.SpotifyImageUrl;
                }
            }

            return topAlbums;
        }

        private async Task<List<AlbumSpotifyCoverDto>> GetCachedAlbumCovers()
        {
            const string cacheKey = "album-spotify-covers";
            if (this._cache.TryGetValue(cacheKey, out List<AlbumSpotifyCoverDto> albumCovers))
            {
                return albumCovers;
            }

            const string sql = "SELECT LOWER(last_fm_url) as last_fm_url, LOWER(spotify_image_url) as spotify_image_url " +
                               "FROM public.albums where last_fm_url is not null and spotify_image_url is not null;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            albumCovers = (await connection.QueryAsync<AlbumSpotifyCoverDto>(sql)).ToList();
            await connection.CloseAsync();

            this._cache.Set(cacheKey, albumCovers, TimeSpan.FromMinutes(1));

            return albumCovers;
        }
    }
}
