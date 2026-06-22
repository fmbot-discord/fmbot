using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public static class TagRepository
{
    public static async Task AddArtistTagsIfMissing(int artistId, IEnumerable<string> tagNames,
        NpgsqlConnection connection)
    {
        var names = NormalizeTagNames(tagNames);
        if (names.Length == 0)
        {
            return;
        }

        const string existsQuery = "SELECT EXISTS(SELECT 1 FROM public.artist_tags WHERE artist_id = @artistId)";
        if (await connection.QueryFirstOrDefaultAsync<bool>(existsQuery, new { artistId }))
        {
            return;
        }

        await UpsertTags(names, connection);

        const string insertQuery =
            "INSERT INTO public.artist_tags (artist_id, tag_id) " +
            "SELECT @artistId, t.id FROM public.tags t " +
            "WHERE t.name = ANY(@names::citext[]) " +
            "ON CONFLICT DO NOTHING";

        await connection.ExecuteAsync(insertQuery, new { artistId, names });
    }

    public static async Task AddAlbumTagsIfMissing(int albumId, IEnumerable<string> tagNames,
        NpgsqlConnection connection)
    {
        var names = NormalizeTagNames(tagNames);
        if (names.Length == 0)
        {
            return;
        }

        const string existsQuery = "SELECT EXISTS(SELECT 1 FROM public.album_tags WHERE album_id = @albumId)";
        if (await connection.QueryFirstOrDefaultAsync<bool>(existsQuery, new { albumId }))
        {
            return;
        }

        await UpsertTags(names, connection);

        const string insertQuery =
            "INSERT INTO public.album_tags (album_id, tag_id) " +
            "SELECT @albumId, t.id FROM public.tags t " +
            "WHERE t.name = ANY(@names::citext[]) " +
            "ON CONFLICT DO NOTHING";

        await connection.ExecuteAsync(insertQuery, new { albumId, names });
    }

    public static async Task AddTrackTagsIfMissing(int trackId, IEnumerable<string> tagNames,
        NpgsqlConnection connection)
    {
        var names = NormalizeTagNames(tagNames);
        if (names.Length == 0)
        {
            return;
        }

        const string existsQuery = "SELECT EXISTS(SELECT 1 FROM public.track_tags WHERE track_id = @trackId)";
        if (await connection.QueryFirstOrDefaultAsync<bool>(existsQuery, new { trackId }))
        {
            return;
        }

        await UpsertTags(names, connection);

        const string insertQuery =
            "INSERT INTO public.track_tags (track_id, tag_id) " +
            "SELECT @trackId, t.id FROM public.tags t " +
            "WHERE t.name = ANY(@names::citext[]) " +
            "ON CONFLICT DO NOTHING";

        await connection.ExecuteAsync(insertQuery, new { trackId, names });
    }

    private static async Task UpsertTags(string[] names, NpgsqlConnection connection)
    {
        const string insertQuery = "INSERT INTO public.tags (name) " +
                                   "SELECT unnest(@names::citext[]) " +
                                   "ON CONFLICT (name) DO NOTHING";

        await connection.ExecuteAsync(insertQuery, new { names });
    }

    private static string[] NormalizeTagNames(IEnumerable<string> tagNames)
    {
        if (tagNames == null)
        {
            return [];
        }

        return tagNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    public static async Task SetTagBanned(string name, bool banned, NpgsqlConnection connection)
    {
        const string upsertQuery = "INSERT INTO public.tags (name, banned) VALUES (@name, @banned) " +
                                   "ON CONFLICT (name) DO UPDATE SET banned = @banned";

        await connection.ExecuteAsync(upsertQuery, new { name, banned });
    }

    public static async Task<List<string>> GetBannedTags(NpgsqlConnection connection)
    {
        const string sql = "SELECT name FROM public.tags WHERE banned ORDER BY name";

        return (await connection.QueryAsync<string>(sql)).ToList();
    }
}
