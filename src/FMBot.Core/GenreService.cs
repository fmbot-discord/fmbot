using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Core;

public class GenreService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public GenreService(IMemoryCache cache, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    private async Task<NpgsqlConnection> OpenConnection()
    {
        var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task<List<TopGenre>> GetTopGenresForUser(int userId)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetTopGenresForUser(userId, connection);
    }

    public async Task<List<TopArtist>> GetUserArtistsForGenre(int userId, string genreName)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetUserArtistsForGenre(userId, genreName, connection);
    }

    public async Task<List<TopGenre>> GetUserArtistsForGenres(int userId, IEnumerable<string> genreNames)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetUserArtistsForGenres(userId, genreNames, connection);
    }

    public async Task<List<GuildGenre>> GetTopGenresForGuildAllTime(int guildId, OrderType orderType, int limit = 240)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetTopGenresForGuildAllTime(guildId, orderType, limit, connection);
    }

    public async Task<List<TopArtist>> GetGuildArtistsForGenre(int guildId, string genreName, int limit = 500)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetGuildArtistsForGenre(guildId, genreName, limit, connection);
    }

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetGuildUsersForGenre(
        int guildId,
        string genreName,
        IDictionary<int, FullGuildUser> guildUsers)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetGuildUsersForGenre(guildId, genreName, guildUsers, connection);
    }

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetFriendUsersForGenre(
        int userId,
        string genreName,
        IDictionary<int, FullGuildUser> guildUsers,
        ICollection<Friend> friends)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetFriendUsersForGenre(userId, genreName, guildUsers, friends, connection);
    }

    public async Task<List<string>> GetGenresForArtist(string artistName)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetGenresForArtist(artistName, connection);
    }

    public async Task<List<TopGenre>> GetTopGenresForTopArtists(IEnumerable<TopArtist> topArtists)
    {
        var artistList = topArtists?.ToList();
        if (artistList == null || artistList.Count == 0)
        {
            return [];
        }

        await using var connection = await OpenConnection();
        return await GenreRepository.GetTopGenresForTopArtists(artistList, connection);
    }

    public List<TopListObject> GetTopListForTopGenres(List<TopGenre> topGenres)
    {
        return topGenres.Select(s => new TopListObject
        {
            Name = s.GenreName,
            Playcount = s.UserPlaycount.GetValueOrDefault()
        }).ToList();
    }

    public async Task<List<AffinityItemDto>> GetTopGenresWithPositionForTopArtists(IEnumerable<AffinityItemDto> topArtists)
    {
        var artistList = topArtists?.ToList();
        if (artistList == null || artistList.Count == 0)
        {
            return new List<AffinityItemDto>();
        }

        await using var connection = await OpenConnection();
        return await GenreRepository.GetTopGenresWithPositionForTopArtists(artistList, connection);
    }

    public async Task<List<string>> GetTopGenresForTopArtistsString(IEnumerable<string> topArtists)
    {
        var artistList = topArtists?.ToList();
        if (artistList == null || artistList.Count == 0)
        {
            return new List<string>();
        }

        await using var connection = await OpenConnection();
        return await GenreRepository.GetTopGenresForTopArtistsString(artistList, connection);
    }

    public async Task<Dictionary<int, List<string>>> GetGenresByArtistIds(IEnumerable<int> artistIds)
    {
        var ids = artistIds.ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, List<string>>();
        }

        await using var connection = await OpenConnection();
        return await GenreRepository.GetGenresByArtistIds(ids, connection);
    }

    public static List<string> GetTopGenresFromPlays(IEnumerable<UserPlay> plays, Dictionary<int, List<string>> genreMap, int amount = 3)
    {
        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var play in plays)
        {
            if (play.ArtistId.HasValue && genreMap.TryGetValue(play.ArtistId.Value, out var genres))
            {
                foreach (var genre in genres)
                {
                    genreCounts[genre] = genreCounts.GetValueOrDefault(genre) + 1;
                }
            }
        }

        return genreCounts
            .OrderByDescending(g => g.Value)
            .Take(amount)
            .Select(g => g.Key)
            .ToList();
    }

    public async Task<List<TopGenre>> GetArtistsForGenres(IEnumerable<string> selectedGenres, List<TopArtist> topArtists)
    {
        await using var connection = await OpenConnection();
        return await GenreRepository.GetArtistsForGenres(selectedGenres, topArtists, connection);
    }

    private async Task<List<string>> GetAllGenreNamesCached()
    {
        const string cacheKey = "genres-all";
        if (this._cache.TryGetValue(cacheKey, out List<string> genres))
        {
            return genres;
        }

        await using var connection = await OpenConnection();
        genres = await GenreRepository.GetAllGenreNames(connection);
        this._cache.Set(cacheKey, genres, TimeSpan.FromHours(2));

        return genres;
    }

    public async Task<List<string>> ResolveGenres(IEnumerable<string> inputs)
    {
        var result = new List<string>();
        if (inputs == null)
        {
            return result;
        }

        var genres = await GetAllGenreNamesCached();

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var query = input.ToLower().Replace(" ", "").Replace("-", "");
            var match = genres.FirstOrDefault(g =>
                            g.Replace(" ", "").Replace("-", "").Equals(query, StringComparison.OrdinalIgnoreCase))
                        ?? genres.FirstOrDefault(g =>
                            g.Replace(" ", "").Replace("-", "").Contains(query, StringComparison.OrdinalIgnoreCase));

            if (match != null && !result.Contains(match, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(match);
            }
        }

        return result;
    }

    public async Task<HashSet<string>> GetArtistsInGenres(IEnumerable<string> artistNames, IEnumerable<string> genreNames)
    {
        var names = artistNames.ToList();
        var genres = genreNames.ToList();
        if (names.Count == 0 || genres.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var connection = await OpenConnection();
        return await GenreRepository.GetArtistsInGenres(names, genres, connection);
    }

    public async Task<List<string>> GetValidGenres(string genreValues)
    {
        if (string.IsNullOrWhiteSpace(genreValues))
        {
            return null;
        }

        await using var connection = await OpenConnection();
        var normalizedArtistGenres = await GenreRepository.GetDistinctGenres(connection);

        var searchQuery = genreValues.ToLower().Replace(" ", "").Replace("-", "");

        var foundGenres = new List<string>();
        var firstResult = normalizedArtistGenres.FirstOrDefault(f => f.Replace(" ", "").Replace("-", "").Equals(searchQuery, StringComparison.OrdinalIgnoreCase));

        if (firstResult != null)
        {
            foundGenres.Add(firstResult);

            foundGenres.ReplaceOrAddToList(normalizedArtistGenres.Where(f => f.Replace(" ", "").Replace("-", "").Contains(searchQuery, StringComparison.OrdinalIgnoreCase)));
        }

        return foundGenres.Take(25).ToList();
    }

    public async Task<List<string>> SearchThroughGenres(string searchValue, bool cacheEnabled = true)
    {
        try
        {
            const string cacheKey = "genres-all";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<string> genres);
            if (!cacheAvailable && cacheEnabled)
            {
                genres = await GetAllGenreNamesCached();
            }

            var results = genres.Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();

            results.AddRange(genres.Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase)));

            return results;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in SearchThroughGenres");
            throw;
        }
    }
}
