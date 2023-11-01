using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Persistence.Repositories;

public class AlbumRepository
{
    private readonly BotSettings _botSettings;

    public AlbumRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public static async Task<ulong> AddOrReplaceUserAlbumsInDatabase(IReadOnlyList<UserAlbum> albums, int userId,
        NpgsqlConnection connection)
    {
        Log.Information("Index: {userId} - Inserting {albumCount} top albums", userId, albums.Count);

        var copyHelper = new PostgreSQLCopyHelper<UserAlbum>("public", "user_albums")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount);

        await using var deleteCurrentAlbums = new NpgsqlCommand($"DELETE FROM public.user_albums WHERE user_id = {userId};", connection);
        await deleteCurrentAlbums.ExecuteNonQueryAsync();

        return await copyHelper.SaveAllAsync(connection, albums);
    }

    public static async Task<Album> GetAlbumForName(string artistName, string albumName, NpgsqlConnection connection)
    {
        const string getAlbumQuery = "SELECT * FROM public.albums " +
                                     "WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                                     "UPPER(name) = UPPER(CAST(@albumName AS CITEXT))";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<Album>(getAlbumQuery, new
        {
            artistName,
            albumName
        });
    }

    public static async Task<IReadOnlyCollection<UserAlbum>> GetUserAlbums(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId
        })).ToList();
    }

    public static async Task<int> GetAlbumPlayCountForUser(NpgsqlConnection connection, string artistName, string albumName, int userId)
    {
        const string sql = "SELECT ua.playcount " +
                           "FROM user_albums AS ua " +
                           "WHERE ua.user_id = @userId AND " +
                           "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND " +
                           "UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY playcount DESC";

        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            albumName,
            artistName
        });
    }
}
