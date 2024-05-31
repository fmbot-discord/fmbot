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

    private async Task CacheAllArtistGenres()
    {
        const string cacheKey = "artist-genres-cached";
        var cacheTime = TimeSpan.FromHours(1);

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

    public async Task<IEnumerable<UserArtist>> GetTopUserArtistsForUserFriendsAsync(int userId, string genreName)
    {
        const string sql = "SELECT ua.user_id, LOWER(ua.name) AS name, ua.playcount " +
                           "FROM user_artists AS ua " +
                           "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                           "INNER JOIN friends AS fr ON fr.friend_user_id = ua.user_id " +
                           "WHERE fr.user_id = @userId " +
                           "AND LOWER(ua.name) = ANY(SELECT LOWER(artists.name) AS artist_name " +
                           "FROM public.artist_genres AS ag " +
                           "INNER JOIN artists ON artists.id = ag.artist_id WHERE LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT))) " +
                           "UNION " +
                           "SELECT ua.user_id, LOWER(ua.name) AS name, ua.playcount " +
                           "FROM user_artists AS ua " +
                           "WHERE ua.user_id = @userId " +
                           "AND LOWER(ua.name) = ANY(SELECT LOWER(artists.name) AS artist_name " +
                           "FROM public.artist_genres AS ag " +
                           "INNER JOIN artists ON artists.id = ag.artist_id WHERE LOWER(ag.name) = LOWER(CAST(@genreName AS CITEXT)))  ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userArtists = await connection.QueryAsync<UserArtist>(sql, new
        {
            userId,
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
            Log.Error("Error in SearchThroughGenres", e);
            throw;
        }
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

        await CacheAllArtistGenres();

        var allGenres = new List<GenreWithPlaycount>();
        foreach (var artist in topArtists)
        {
            allGenres = GetGenreWithPlaycountsForArtist(allGenres, artist.Name, artist.Playcount);
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
        var topGenres = new List<string>();
        if (topArtists == null)
        {
            return topGenres;
        }

        await CacheAllArtistGenres();

        foreach (var artist in topArtists)
        {
            var genres = await GetGenresForArtist(artist);
            if (genres != null && genres.Any())
            {
                topGenres.AddRange(genres);
            }
        }

        return topGenres
            .GroupBy(g => g)
            .OrderByDescending(o => o.Count())
            .Where(w => w.Key != null)
            .Select(s => s.Key)
            .ToList();
    }

    public async Task<List<GuildGenre>> GetTopGenresForGuildArtists(IEnumerable<GuildArtist> guildArtists, OrderType orderType)
    {
        if (guildArtists == null)
        {
            return new List<GuildGenre>();
        }

        await CacheAllArtistGenres();

        var allGenresWithListeners = new List<GenreWithListeners>();
        foreach (var artist in guildArtists)
        {
            var foundGenres = (List<string>)this._cache.Get(CacheKeyForArtistGenres(artist.ArtistName.ToLower()));

            if (foundGenres != null && foundGenres.Any())
            {
                foreach (var genre in foundGenres)
                {
                    if (artist.TotalPlaycount > 0)
                    {
                        allGenresWithListeners.Add(new GenreWithListeners(genre, artist.TotalPlaycount, artist.ListenerUserIds));
                    }
                }
            }
        }

        return allGenresWithListeners
            .GroupBy(g => g.Name)
            .OrderByDescending(o => o.Sum(s => s.Playcount))
            .Where(w => w.Key != null)
            .Select(s => new GuildGenre
            {
                GenreName = s.Key,
                TotalPlaycount = s.Sum(se => se.Playcount),
                ListenerCount = s.SelectMany(se => se.UserIds).Distinct().Count()
            })
            .OrderByDescending(o => orderType == OrderType.Listeners ? o.ListenerCount : o.TotalPlaycount)
            .Take(240)
            .ToList();
    }

    private record GenreWithListeners(string Name, long Playcount, List<int> UserIds);

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetUsersWithGenreForUserArtists(
        IEnumerable<UserArtist> userArtists,
        IDictionary<int, FullGuildUser> guildUsers,
        ICollection<Friend> contextUserFriends = null)
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
                if (guildUsers.TryGetValue(user.UserId, out var guildUser))
                {
                    list.Add(new WhoKnowsObjectWithUser
                    {
                        UserId = user.UserId,
                        Playcount = user.Playcount,
                        DiscordName = guildUser.UserName,
                        LastFMUsername = guildUser.UserNameLastFM,
                        Name = guildUser.UserName,
                        LastUsed = guildUser.LastUsed,
                        LastMessage = guildUser.LastMessage,
                        Roles = guildUser.Roles
                    });

                }
                else if (contextUserFriends != null &&
                         contextUserFriends.Any(a => a.FriendUserId == user.UserId))
                {
                    var friend = contextUserFriends.First(f => f.FriendUserId == user.UserId);
                    list.Add(new WhoKnowsObjectWithUser
                    {
                        UserId = user.UserId,
                        Playcount = user.Playcount,
                        DiscordName = friend.FriendUser.UserNameLastFM,
                        LastFMUsername = friend.FriendUser.UserNameLastFM,
                        Name = friend.FriendUser.UserNameLastFM,
                        LastUsed = friend.FriendUser.LastUsed,
                    });
                }
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
                        .Where(w => artistGenres.Any(a => a.Equals(w.ArtistName.ToLower())))
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

    public static string GenresToString(IEnumerable<ArtistGenre> genres)
    {
        return StringService.StringListToLongString(genres.Select(s => s.Name).ToList());
    }
}
