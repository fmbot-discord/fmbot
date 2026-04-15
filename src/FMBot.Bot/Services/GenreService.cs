using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class GenreService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public GenreService(IMemoryCache cache, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public async Task<List<TopGenre>> GetTopGenresForUser(int userId)
    {
        const string sql = "SELECT ag.name AS GenreName, SUM(ua.playcount)::bigint AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = ua.artist_id " +
                           "WHERE ua.user_id = @userId AND ua.artist_id IS NOT NULL " +
                           "GROUP BY ag.name " +
                           "ORDER BY UserPlaycount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<TopGenre>(sql, new { userId })).ToList();
    }

    public async Task<List<TopArtist>> GetUserArtistsForGenre(int userId, string genreName)
    {
        const string sql = "SELECT ua.name AS ArtistName, ua.playcount AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = ua.artist_id " +
                           "WHERE ua.user_id = @userId AND ua.artist_id IS NOT NULL " +
                           "AND LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)) " +
                           "ORDER BY ua.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<TopArtist>(sql, new { userId, genreName })).ToList();
    }

    public async Task<List<TopGenre>> GetUserArtistsForGenres(int userId, IEnumerable<string> genreNames)
    {
        const string sql = "SELECT ag.name AS Genre, ua.name AS ArtistName, ua.playcount AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = ua.artist_id " +
                           "WHERE ua.user_id = @userId AND ua.artist_id IS NOT NULL " +
                           "AND LOWER(ag.name) = ANY(@genreNamesLower) " +
                           "ORDER BY ua.playcount DESC";

        var genreNamesLower = genreNames.Select(g => g.ToLower()).ToArray();

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var rows = (await connection.QueryAsync<(string Genre, string ArtistName, long UserPlaycount)>(sql,
            new { userId, genreNamesLower })).ToList();

        return rows
            .GroupBy(r => r.Genre, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopGenre
            {
                GenreName = g.Key,
                Artists = g.Select(r => new TopArtist
                {
                    ArtistName = r.ArtistName,
                    UserPlaycount = r.UserPlaycount
                }).OrderByDescending(a => a.UserPlaycount).ToList()
            })
            .ToList();
    }

    public async Task<List<GuildGenre>> GetTopGenresForGuildAllTime(int guildId, OrderType orderType, int limit = 240)
    {
        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT ag.name AS GenreName, " +
                  "       SUM(ua.playcount)::bigint AS TotalPlaycount, " +
                  "       COUNT(DISTINCT ua.user_id)::bigint AS ListenerCount " +
                  "FROM user_artists ua " +
                  "INNER JOIN guild_users gu ON gu.user_id = ua.user_id " +
                  "INNER JOIN artist_genres ag ON ag.artist_id = ua.artist_id " +
                  "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                  "AND ua.artist_id IS NOT NULL " +
                  "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "GROUP BY ag.name " +
                  $"ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "LIMIT @limit";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<GuildGenre>(sql, new { guildId, limit })).ToList();
    }

    public async Task<List<TopArtist>> GetGuildArtistsForGenre(int guildId, string genreName, int limit = 500)
    {
        const string sql = "SELECT ua.name AS ArtistName, SUM(ua.playcount)::bigint AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN guild_users gu ON gu.user_id = ua.user_id " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = ua.artist_id " +
                           "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                           "AND ua.artist_id IS NOT NULL " +
                           "AND LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)) " +
                           "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                           "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                           "GROUP BY ua.name " +
                           "ORDER BY UserPlaycount DESC " +
                           "LIMIT @limit";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<TopArtist>(sql, new { guildId, genreName, limit })).ToList();
    }

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetGuildUsersForGenre(
        int guildId,
        string genreName,
        IDictionary<int, FullGuildUser> guildUsers)
    {
        const string sql = "SELECT ua.user_id AS UserId, SUM(ua.playcount) AS Playcount " +
                           "FROM user_artists ua " +
                           "INNER JOIN guild_users gu ON gu.user_id = ua.user_id " +
                           "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                           "AND ua.artist_id IN ( " +
                           "    SELECT ag.artist_id FROM artist_genres ag " +
                           "    WHERE LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)) " +
                           ") " +
                           "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                           "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                           "GROUP BY ua.user_id " +
                           "ORDER BY Playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userPlaycounts = (await connection.QueryAsync<(int UserId, int Playcount)>(sql,
            new { guildId, genreName })).ToList();

        var list = new List<WhoKnowsObjectWithUser>();
        foreach (var (userId, playcount) in userPlaycounts)
        {
            if (guildUsers != null && guildUsers.TryGetValue(userId, out var guildUser))
            {
                list.Add(new WhoKnowsObjectWithUser
                {
                    UserId = userId,
                    Playcount = playcount,
                    DiscordName = guildUser.UserName,
                    LastFMUsername = guildUser.UserNameLastFM,
                    Name = guildUser.UserName,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles
                });
            }
        }

        return list;
    }

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetFriendUsersForGenre(
        int userId,
        string genreName,
        IDictionary<int, FullGuildUser> guildUsers,
        ICollection<Friend> friends)
    {
        const string sql = "SELECT ua.user_id AS UserId, SUM(ua.playcount) AS Playcount " +
                           "FROM user_artists ua " +
                           "WHERE ua.user_id = ANY(@userIds) " +
                           "AND ua.artist_id IN ( " +
                           "    SELECT ag.artist_id FROM artist_genres ag " +
                           "    WHERE LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)) " +
                           ") " +
                           "GROUP BY ua.user_id " +
                           "ORDER BY Playcount DESC";

        var friendUserIds = friends?.Where(f => f.FriendUserId.HasValue).Select(f => f.FriendUserId.Value).ToList()
                            ?? new List<int>();
        friendUserIds.Add(userId);
        var userIds = friendUserIds.ToArray();

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userPlaycounts = (await connection.QueryAsync<(int UserId, int Playcount)>(sql,
            new { userIds, genreName })).ToList();

        var list = new List<WhoKnowsObjectWithUser>();
        foreach (var (uid, playcount) in userPlaycounts)
        {
            if (guildUsers != null && guildUsers.TryGetValue(uid, out var guildUser))
            {
                list.Add(new WhoKnowsObjectWithUser
                {
                    UserId = uid,
                    Playcount = playcount,
                    DiscordName = guildUser.UserName,
                    LastFMUsername = guildUser.UserNameLastFM,
                    Name = guildUser.UserName,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles
                });
            }
            else if (friends != null && friends.Any(a => a.FriendUserId == uid))
            {
                var friend = friends.First(f => f.FriendUserId == uid);
                list.Add(new WhoKnowsObjectWithUser
                {
                    UserId = uid,
                    Playcount = playcount,
                    DiscordName = friend.FriendUser.UserNameLastFM,
                    LastFMUsername = friend.FriendUser.UserNameLastFM,
                    Name = friend.FriendUser.UserNameLastFM,
                    LastUsed = friend.FriendUser.LastUsed,
                });
            }
        }

        return list;
    }

    public async Task<List<string>> GetGenresForArtist(string artistName)
    {
        const string sql = "SELECT ag.name " +
                           "FROM artist_genres ag " +
                           "INNER JOIN artists a ON a.id = ag.artist_id " +
                           "WHERE UPPER(a.name) = UPPER(CAST(@artistName AS CITEXT))";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var result = (await connection.QueryAsync<string>(sql, new { artistName })).ToList();
        return result.Count != 0 ? result : null;
    }

    public async Task<List<TopGenre>> GetTopGenresForTopArtists(IEnumerable<TopArtist> topArtists)
    {
        if (topArtists == null)
        {
            return [];
        }

        var artistList = topArtists.ToList();
        if (artistList.Count == 0)
        {
            return [];
        }

        var artistNames = artistList.Select(a => a.ArtistName).Distinct().ToArray();

        const string sql = "SELECT ag.name AS Genre, a.name AS ArtistName " +
                           "FROM artists a " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = a.id " +
                           "WHERE a.name = ANY(@artistNames::citext[])";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var genreMappings = (await connection.QueryAsync<(string Genre, string ArtistName)>(sql,
            new { artistNames })).ToList();

        var artistGenreMap = genreMappings
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Genre).ToList(), StringComparer.OrdinalIgnoreCase);

        var allGenres = new List<GenreWithPlaycount>();
        foreach (var artist in artistList)
        {
            if (artistGenreMap.TryGetValue(artist.ArtistName, out var genres))
            {
                foreach (var genre in genres)
                {
                    var playcount = artist.UserPlaycount;
                    if (playcount > 0)
                    {
                        allGenres.Add(new GenreWithPlaycount(genre, playcount));
                    }
                }
            }
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
        if (topArtists == null)
        {
            return new List<AffinityItemDto>();
        }

        var artistList = topArtists.ToList();
        if (!artistList.Any())
        {
            return new List<AffinityItemDto>();
        }

        var artistNames = artistList.Select(a => a.Name).Distinct().ToArray();

        const string sql = "SELECT ag.name AS Genre, a.name AS ArtistName " +
                           "FROM artists a " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = a.id " +
                           "WHERE a.name = ANY(@artistNames::citext[])";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var genreMappings = (await connection.QueryAsync<(string Genre, string ArtistName)>(sql,
            new { artistNames })).ToList();

        var artistGenreMap = genreMappings
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Genre).ToList(), StringComparer.OrdinalIgnoreCase);

        var allGenres = new List<GenreWithPlaycount>();
        foreach (var artist in artistList)
        {
            if (artistGenreMap.TryGetValue(artist.Name, out var genres))
            {
                foreach (var genre in genres)
                {
                    if (artist.Playcount > 0)
                    {
                        allGenres.Add(new GenreWithPlaycount(genre, artist.Playcount));
                    }
                }
            }
        }

        return allGenres
            .GroupBy(g => g.Name)
            .OrderByDescending(o => o.Sum(s => s.Playcount))
            .Where(w => w.Key != null)
            .Select((s, i) => new AffinityItemDto
            {
                Name = s.Key,
                Playcount = s.Sum(se => se.Playcount),
                Position = i
            }).ToList();
    }

    public async Task<List<string>> GetTopGenresForTopArtistsString(IEnumerable<string> topArtists)
    {
        if (topArtists == null)
        {
            return new List<string>();
        }

        var artistNames = topArtists.Distinct().ToArray();
        if (artistNames.Length == 0)
        {
            return new List<string>();
        }

        const string sql = "SELECT ag.name AS Genre, a.name AS ArtistName " +
                           "FROM artists a " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = a.id " +
                           "WHERE a.name = ANY(@artistNames::citext[])";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var genreMappings = (await connection.QueryAsync<(string Genre, string ArtistName)>(sql,
            new { artistNames })).ToList();

        return genreMappings
            .GroupBy(g => g.Genre)
            .OrderByDescending(o => o.Count())
            .Where(w => w.Key != null)
            .Select(s => s.Key)
            .ToList();
    }

    public async Task<List<string>> GetTopGenresForPlays(IEnumerable<UserPlay> plays, int amount = 3)
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
            .Take(amount)
            .ToList();
    }

    public async Task<List<TopGenre>> GetArtistsForGenres(IEnumerable<string> selectedGenres, List<TopArtist> topArtists)
    {
        var genreList = selectedGenres.ToList();
        var artistNames = topArtists.Select(a => a.ArtistName).Distinct().ToArray();
        var genreNames = genreList.ToArray();

        const string sql = "SELECT ag.name AS Genre, a.name AS ArtistName " +
                           "FROM artists a " +
                           "INNER JOIN artist_genres ag ON ag.artist_id = a.id " +
                           "WHERE a.name = ANY(@artistNames::citext[]) " +
                           "AND ag.name = ANY(@genreNames::citext[])";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var genreMappings = (await connection.QueryAsync<(string Genre, string ArtistName)>(sql,
            new { artistNames, genreNames })).ToList();

        var genreArtistMap = genreMappings
            .GroupBy(g => g.Genre, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(x => x.ArtistName), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var foundGenres = new List<TopGenre>();
        foreach (var selectedGenre in genreList)
        {
            if (genreArtistMap.TryGetValue(selectedGenre, out var artistsInGenre))
            {
                foundGenres.Add(new TopGenre
                {
                    GenreName = selectedGenre,
                    Artists = topArtists
                        .Where(w => artistsInGenre.Contains(w.ArtistName))
                        .OrderByDescending(o => o.UserPlaycount)
                        .ToList()
                });
            }
        }

        return foundGenres;
    }

    public async Task<List<string>> GetValidGenres(string genreValues)
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

        var normalizedArtistGenres = artistGenres
            .Select(s => s.Genre)
            .ToList();

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
                const string sql = "SELECT DISTINCT name " +
                                   "FROM public.artist_genres ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                genres = (await connection.QueryAsync<string>(sql)).ToList();

                this._cache.Set(cacheKey, genres, TimeSpan.FromHours(2));
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

    private record GenreWithPlaycount(string Name, long Playcount);

    public static string GenresToString(IEnumerable<ArtistGenre> genres)
    {
        return StringService.StringListToLongString(genres.Select(s => s.Name).ToList());
    }
}
