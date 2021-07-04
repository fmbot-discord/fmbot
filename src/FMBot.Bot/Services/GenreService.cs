using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services
{
    public class GenreService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;


        public GenreService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        private async Task<List<ArtistGenreDto>> GetCachedArtistGenres()
        {
            const string cacheKey = "artist-genres";
            if (this._cache.TryGetValue(cacheKey, out List<ArtistGenreDto> artistGenres))
            {
                return artistGenres;
            }

            const string sql = "SELECT ag.name AS genre, LOWER(artists.name) AS artist_name " +
                               "FROM public.artist_genres AS ag " +
                               "INNER JOIN artists ON artists.id = ag.artist_id;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            artistGenres = (await connection.QueryAsync<ArtistGenreDto>(sql)).ToList();

            this._cache.Set(cacheKey, artistGenres, TimeSpan.FromMinutes(5));

            return artistGenres;
        }

        public async Task<List<TopGenre>> GetTopGenresForTopArtists(IEnumerable<TopArtist> topArtists)
        {
            var genres = await GetCachedArtistGenres();

            var allGenres = new List<string>();
            Parallel.ForEach(topArtists, artist =>
            {
                var foundGenres = genres
                    .Where(item => item.ArtistName.Equals(artist.ArtistName.ToLower()))
                    .ToList();

                if (foundGenres.Any())
                {
                    allGenres.AddRange(foundGenres.Select(s => s.Genre));
                }
            });

            return allGenres
                .GroupBy(g => g)
                .OrderByDescending(o => o.Count())
                .Select(s => new TopGenre
                {
                    UserPlaycount = s.Count(),
                    GenreName = s.Key
                }).ToList();
        }

        public static string GenresToString(List<ArtistGenre> genres)
        {
            var genreString = new StringBuilder();
            for (var i = 0; i < genres.Count; i++)
            {
                if (i != 0)
                {
                    genreString.Append(" - ");
                }

                var genre = genres[i];
                genreString.Append($"{genre.Name}");
            }

            return genreString.ToString();
        }
    }
}
