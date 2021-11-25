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
                this._cache.Set(CacheKeyForArtistGenres(artist.Key), genres, cacheTime);
            }
            foreach (var genre in artistGenres.GroupBy(g => g.Genre))
            {
                var artists = genre.Select(s => s.ArtistName).ToList();
                this._cache.Set(CacheKeyForGenreArtists(genre.Key), artists, cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        public async Task<IEnumerable<UserArtist>> GetTopUserArtistsForGuildAsync(int guildId, string genreName)
        {

            const string sql = "SELECT ua.user_id, " +
                               "LOWER(ua.name) AS name, " +
                               "ua.playcount " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                               "AND LOWER(ua.name) = ANY(SELECT LOWER(artists.name) AS artist_name " +
                               "    FROM public.artist_genres AS ag " +
                               "    INNER JOIN artists ON artists.id = ag.artist_id WHERE LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)))  " +
                               "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                               "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL)   ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = await connection.QueryAsync<UserArtist>(sql, new
            {
                guildId,
                genreName
            });

            return userArtists;
        }

        private static string CacheKeyForArtistGenres(string artistName)
        {
            return $"artist-genres-{artistName}";
        }
        private static string CacheKeyForGenreArtists(string genreName)
        {
            return $"genre-artists-{genreName}";
        }

        public async Task<string> GetValidGenre(string genreValues)
        {
            if (string.IsNullOrWhiteSpace(genreValues))
            {
                return null;
            }

            const string sql = "SELECT DISTINCT ag.name AS genre " +
                               "FROM public.artist_genres AS ag ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var artistGenres = (await connection.QueryAsync<ArtistGenreDto>(sql)).ToList();

            var searchQuery = genreValues.ToLower().Replace(" ", "").Replace("-", "");

            var foundGenre = artistGenres
                .FirstOrDefault(f => f.Genre.Replace(" ", "").Replace("-", "") == searchQuery);

            return foundGenre?.Genre;
        }

        public async Task<List<TopGenre>> GetTopGenresForTopArtists(IEnumerable<TopArtist> topArtists)
        {
            if (topArtists == null)
            {
                return new List<TopGenre>();
            }

            await CacheAllArtistGenres();

            var allGenres = new List<GenreWithPlaycount>();
            foreach (var artist in topArtists)
            {
                allGenres = GetGenreWithPlaycountsForArtist(allGenres, artist.ArtistName, artist.UserPlaycount);
            }

            return allGenres
                .GroupBy(g => g.Name)
                .OrderByDescending(o => o.Sum(s => s.Playcount))
                .Where(w => w.Key != null)
                .Select(s => new TopGenre
                {
                    UserPlaycount = s.Sum(se => se.Playcount),
                    GenreName = s.Key
                }).ToList();
        }

        public async Task<ICollection<WhoKnowsObjectWithUser>> GetUsersWithGenreForUserArtists(
            IEnumerable<UserArtist> userArtists,
            ICollection<GuildUser> guildUsers)
        {
            await CacheAllArtistGenres();

            var list = new List<WhoKnowsObjectWithUser>();

            foreach (var user in userArtists)
            {
                var existingEntry = list.FirstOrDefault(f => f.UserId == user.UserId);
                if (existingEntry != null)
                {
                    existingEntry.Playcount += user.Playcount;
                }
                else
                {
                    var guildUser = guildUsers.FirstOrDefault(f => f.UserId == user.UserId);
                    if (guildUser == null)
                    {
                        continue;
                    }

                    list.Add(new WhoKnowsObjectWithUser
                    {
                        UserId = user.UserId,
                        Playcount = user.Playcount,
                        DiscordName = guildUser.UserName,
                        LastFMUsername = guildUser.User.UserNameLastFM,
                        Name = guildUser.UserName,
                        PrivacyLevel = guildUser.User.PrivacyLevel,
                        RegisteredLastFm = guildUser.User.RegisteredLastFm,
                        WhoKnowsWhitelisted = guildUser.WhoKnowsWhitelisted
                    });
                }
            }

            return list
                .OrderByDescending(o => o.Playcount)
                .ToList();
        }

        private List<GenreWithPlaycount> GetGenreWithPlaycountsForArtist(List<GenreWithPlaycount> genres, string artistName, long? artistPlaycount)
        {
            var foundGenres = (List<string>)this._cache.Get(CacheKeyForArtistGenres(artistName.ToLower()));

            if (foundGenres != null && foundGenres.Any())
            {
                foreach (var genre in foundGenres)
                {
                    var playcount = artistPlaycount.GetValueOrDefault();

                    if (playcount > 0)
                    {
                        genres.Add(new GenreWithPlaycount(genre, playcount));
                    }
                }
            }

            return genres;
        }

        private record GenreWithPlaycount(string Name, long Playcount);

        public async Task<List<string>> GetGenresForArtist(string artistName)
        {
            await CacheAllArtistGenres();
            return (List<string>)this._cache.Get(CacheKeyForArtistGenres(artistName.ToLower()));
        }

        public async Task<List<TopGenre>> GetArtistsForGenres(IEnumerable<string> selectedGenres, List<TopArtist> topArtists)
        {
            await CacheAllArtistGenres();

            var foundGenres = new List<TopGenre>();
            foreach (var selectedGenre in selectedGenres)
            {
                var artistGenres = (List<string>)this._cache.Get(CacheKeyForGenreArtists(selectedGenre.ToLower()));

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
