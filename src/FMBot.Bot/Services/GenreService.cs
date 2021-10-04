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

        private async Task CacheAllArtistGenres()
        {
            const string cacheKey = "artist-genres-cached";
            var cacheTime = TimeSpan.FromMinutes(5);

            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            const string sql = "SELECT ag.name AS genre, LOWER(artists.name) AS artist_name " +
                               "FROM public.artist_genres AS ag " +
                               "INNER JOIN artists ON artists.id = ag.artist_id;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var artistGenres = (await connection.QueryAsync<ArtistGenreDto>(sql)).ToList();

            foreach (var artist in artistGenres.GroupBy(g => g.ArtistName))
            {
                var genres = artist.Select(s => s.Genre).ToList();
                this._cache.Set(CacheKeyForArtist(artist.Key), genres, cacheTime);
            }
            foreach (var genre in artistGenres.GroupBy(g => g.Genre))
            {
                var artists = genre.Select(s => s.ArtistName).ToList();
                this._cache.Set(CacheKeyForGenre(genre.Key), artists, cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        private static string CacheKeyForArtist(string artistName)
        {
            return $"artist-genres-{artistName}";
        }
        private static string CacheKeyForGenre(string genreName)
        {
            return $"genre-artists-{genreName}";
        }

        public async Task<List<TopGenre>> GetTopGenresForTopArtists(IEnumerable<TopArtist> topArtists)
        {
            await CacheAllArtistGenres();

            var allGenres = new List<GenreWithPlaycount>();
            foreach (var artist in topArtists)
            {
                var foundGenres = (List<string>)this._cache.Get(CacheKeyForArtist(artist.ArtistName.ToLower()));

                if (foundGenres != null && foundGenres.Any())
                {
                    foreach (var genre in foundGenres)
                    {
                        var playcount = artist.UserPlaycount.GetValueOrDefault();
                        var score = playcount switch
                        {
                            0 => 0,
                            > 3 and < 16 => playcount * 0.3,
                            >= 16 and < 28 => playcount * 0.25,
                            >= 28 and < 40 => playcount * 0.2,
                            >= 40 and < 80 => 10,
                            >= 80 and < 150 => 15,
                            >= 150 and < 300 => 20,
                            >= 300 and < 600 => 30,
                            >= 600 and < 1500 => 40,
                            >= 1500 => 50,
                            _ => 0.5
                        };

                        if (score > 0)
                        {
                            allGenres.Add(new GenreWithPlaycount(genre, score));
                        }
                    }
                }
            }

            return allGenres
                .GroupBy(g => g.Name)
                .OrderByDescending(o => o.Sum(s => s.Score))
                .Where(w => w.Key != null)
                .Select(s => new TopGenre
                {
                    UserPlaycount = s.Count(),
                    GenreName = s.Key
                }).ToList();
        }

        private record GenreWithPlaycount(string Name, double Score);

        public async Task<List<string>> GetGenresForArtist(string artistName)
        {
            await CacheAllArtistGenres();
            return (List<string>)this._cache.Get(CacheKeyForArtist(artistName.ToLower()));
        }

        public async Task<List<TopGenre>> GetArtistsForGenres(IEnumerable<string> selectedGenres, List<TopArtist> topArtists)
        {
            await CacheAllArtistGenres();

            var foundGenres = new List<TopGenre>();
            foreach (var selectedGenre in selectedGenres)
            {
                var artistGenres = (List<string>)this._cache.Get(CacheKeyForGenre(selectedGenre.ToLower()));

                if (artistGenres != null && artistGenres.Any())
                {
                    foundGenres.Add(new TopGenre
                    {
                        GenreName = selectedGenre,
                        Artists = topArtists
                            .Where(w => artistGenres.Any(a => a.ToLower().Equals(w.ArtistName.ToLower())))
                            .OrderByDescending(o => o.UserPlaycount)
                            .ToList()
                    });
                };
            }

            return foundGenres;
        }

        public async Task<List<string>> GetTopGenresForPlays(IEnumerable<UserPlay> plays)
        {
            var artists = plays
                .GroupBy(x => new { x.ArtistName })
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key.ArtistName,
                    UserPlaycount = s.Count()
                });

            var result = await GetTopGenresForTopArtists(artists);

            if (!result.Any())
            {
                return new List<string>();
            }

            return result
                .OrderByDescending(o => o.UserPlaycount)
                .Select(s => s.GenreName)
                .Take(3)
                .ToList();
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
