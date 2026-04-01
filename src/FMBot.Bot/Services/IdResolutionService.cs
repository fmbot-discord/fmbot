using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Web.InternalApi;

namespace FMBot.Bot.Services;

public class IdResolutionService(IdResolution.IdResolutionClient client, IOptions<BotSettings> botSettings, IMemoryCache cache)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    public async Task ResolvePlayIds(IReadOnlyList<UserPlay> plays)
    {
        if (plays == null || plays.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolvePlayIdsRequest();
            for (var i = 0; i < plays.Count; i++)
            {
                request.Plays.Add(new PlayToResolve
                {
                    Index = i,
                    ArtistName = plays[i].ArtistName ?? "",
                    AlbumName = plays[i].AlbumName ?? "",
                    TrackName = plays[i].TrackName ?? ""
                });
            }

            var reply = await client.ResolvePlayIdsAsync(request);

            foreach (var resolved in reply.Plays)
            {
                if (resolved.Index >= 0 && resolved.Index < plays.Count)
                {
                    var play = plays[resolved.Index];
                    if (resolved.ArtistId != 0) play.ArtistId = resolved.ArtistId;
                    if (resolved.AlbumId != 0) play.AlbumId = resolved.AlbumId;
                    if (resolved.TrackId != 0) play.TrackId = resolved.TrackId;
                }
            }

            Log.Debug("IdResolution: Resolved IDs for {playCount} plays", plays.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve play IDs, continuing without IDs");
        }
    }

    public async Task ResolveArtistIds(IReadOnlyList<UserArtist> artists)
    {
        if (artists == null || artists.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveArtistIdsRequest();
            request.ArtistNames.AddRange(artists.Select(a => a.Name ?? ""));

            var reply = await client.ResolveArtistIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in reply.Mappings.Where(m => m.ArtistId != 0))
            {
                mappings.TryAdd(m.ArtistName, m.ArtistId);
            }

            foreach (var artist in artists)
            {
                if (artist.Name != null && mappings.TryGetValue(artist.Name, out var artistId))
                {
                    artist.ArtistId = artistId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} artist IDs",
                mappings.Count, artists.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve artist IDs, continuing without IDs");
        }
    }

    public async Task ResolveAlbumIds(IReadOnlyList<UserAlbum> albums)
    {
        if (albums == null || albums.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveAlbumIdsRequest();
            request.Albums.AddRange(albums.Select(a => new AlbumKey
            {
                ArtistName = a.ArtistName ?? "",
                AlbumName = a.Name ?? ""
            }));

            var reply = await client.ResolveAlbumIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in reply.Mappings.Where(m => m.AlbumId != 0))
            {
                mappings.TryAdd($"{m.ArtistName?.ToLowerInvariant()}\0{m.AlbumName?.ToLowerInvariant()}", m.AlbumId);
            }

            foreach (var album in albums)
            {
                var key = $"{album.ArtistName?.ToLowerInvariant()}\0{album.Name?.ToLowerInvariant()}";
                if (mappings.TryGetValue(key, out var albumId))
                {
                    album.AlbumId = albumId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} album IDs",
                mappings.Count, albums.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve album IDs, continuing without IDs");
        }
    }

    public async Task ResolveTrackIds(IReadOnlyList<UserTrack> tracks)
    {
        if (tracks == null || tracks.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveTrackIdsRequest();
            request.Tracks.AddRange(tracks.Select(t => new TrackKey
            {
                ArtistName = t.ArtistName ?? "",
                TrackName = t.Name ?? ""
            }));

            var reply = await client.ResolveTrackIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in reply.Mappings.Where(m => m.TrackId != 0))
            {
                mappings.TryAdd($"{m.ArtistName?.ToLowerInvariant()}\0{m.TrackName?.ToLowerInvariant()}", m.TrackId);
            }

            foreach (var track in tracks)
            {
                var key = $"{track.ArtistName?.ToLowerInvariant()}\0{track.Name?.ToLowerInvariant()}";
                if (mappings.TryGetValue(key, out var trackId))
                {
                    track.TrackId = trackId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} track IDs",
                mappings.Count, tracks.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve track IDs, continuing without IDs");
        }
    }

    public async Task BackfillUserPlayIds(int userId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM public.user_plays " +
                               "WHERE user_id = @userId AND (" +
                               "(artist_id IS NULL AND artist_name IS NOT NULL) OR " +
                               "(album_id IS NULL AND album_name IS NOT NULL) OR " +
                               "(track_id IS NULL AND track_name IS NOT NULL)) " +
                               "ORDER BY time_played DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            var playsToBackfill = (await connection.QueryAsync<UserPlay>(sql, new { userId })).AsList();

            if (playsToBackfill.Count == 0)
            {
                return;
            }

            await ResolvePlayIds(playsToBackfill);

            const int batchSize = 500;
            var updated = 0;

            for (var i = 0; i < playsToBackfill.Count; i += batchSize)
            {
                var batch = playsToBackfill.Skip(i).Take(batchSize)
                    .Where(p => p.ArtistId.HasValue || p.AlbumId.HasValue || p.TrackId.HasValue)
                    .ToList();

                if (batch.Count == 0)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var play in batch)
                {
                    var setClauses = new List<string>();
                    if (play.ArtistId.HasValue)
                    {
                        setClauses.Add($"artist_id = {play.ArtistId.Value}");
                    }

                    if (play.AlbumId.HasValue)
                    {
                        setClauses.Add($"album_id = {play.AlbumId.Value}");
                    }

                    if (play.TrackId.HasValue)
                    {
                        setClauses.Add($"track_id = {play.TrackId.Value}");
                    }

                    sb.Append($"UPDATE public.user_plays SET {string.Join(", ", setClauses)} " +
                              $"WHERE user_play_id = {play.UserPlayId}; ");
                }

                await connection.ExecuteAsync(sb.ToString());
                updated += batch.Count;
            }

            Log.Information("BackfillPlayIds: Updated {updatedCount}/{totalCount} plays for user {userId}",
                updated, playsToBackfill.Count, userId);
        }
        catch (Exception e)
        {
            Log.Error(e, "BackfillPlayIds: Error backfilling play IDs for user {userId}", userId);
        }
    }

    public async Task BackfillUserArtistIds(int userId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM public.user_artists " +
                               "WHERE user_id = @userId AND artist_id IS NULL AND name IS NOT NULL";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            var artistsToBackfill = (await connection.QueryAsync<UserArtist>(sql, new { userId })).AsList();

            if (artistsToBackfill.Count == 0)
            {
                return;
            }

            await ResolveArtistIds(artistsToBackfill);

            const int batchSize = 500;
            var updated = 0;

            for (var i = 0; i < artistsToBackfill.Count; i += batchSize)
            {
                var batch = artistsToBackfill.Skip(i).Take(batchSize)
                    .Where(a => a.ArtistId.HasValue)
                    .ToList();

                if (batch.Count == 0)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var artist in batch)
                {
                    sb.Append($"UPDATE public.user_artists SET artist_id = {artist.ArtistId.Value} " +
                              $"WHERE user_artist_id = {artist.UserArtistId}; ");
                }

                await connection.ExecuteAsync(sb.ToString());
                updated += batch.Count;
            }

            Log.Information("BackfillArtistIds: Updated {updatedCount}/{totalCount} user artists for user {userId}",
                updated, artistsToBackfill.Count, userId);
        }
        catch (Exception e)
        {
            Log.Error(e, "BackfillArtistIds: Error backfilling artist IDs for user {userId}", userId);
        }
    }

    public async Task BackfillUserAlbumIds(int userId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM public.user_albums " +
                               "WHERE user_id = @userId AND album_id IS NULL AND name IS NOT NULL";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            var albumsToBackfill = (await connection.QueryAsync<UserAlbum>(sql, new { userId })).AsList();

            if (albumsToBackfill.Count == 0)
            {
                return;
            }

            await ResolveAlbumIds(albumsToBackfill);

            const int batchSize = 500;
            var updated = 0;

            for (var i = 0; i < albumsToBackfill.Count; i += batchSize)
            {
                var batch = albumsToBackfill.Skip(i).Take(batchSize)
                    .Where(a => a.AlbumId.HasValue)
                    .ToList();

                if (batch.Count == 0)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var album in batch)
                {
                    sb.Append($"UPDATE public.user_albums SET album_id = {album.AlbumId.Value} " +
                              $"WHERE user_album_id = {album.UserAlbumId}; ");
                }

                await connection.ExecuteAsync(sb.ToString());
                updated += batch.Count;
            }

            Log.Information("BackfillAlbumIds: Updated {updatedCount}/{totalCount} user albums for user {userId}",
                updated, albumsToBackfill.Count, userId);
        }
        catch (Exception e)
        {
            Log.Error(e, "BackfillAlbumIds: Error backfilling album IDs for user {userId}", userId);
        }
    }

    public async Task BackfillUserTrackIds(int userId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM public.user_tracks " +
                               "WHERE user_id = @userId AND track_id IS NULL AND name IS NOT NULL";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            var tracksToBackfill = (await connection.QueryAsync<UserTrack>(sql, new { userId })).AsList();

            if (tracksToBackfill.Count == 0)
            {
                return;
            }

            await ResolveTrackIds(tracksToBackfill);

            const int batchSize = 500;
            var updated = 0;

            for (var i = 0; i < tracksToBackfill.Count; i += batchSize)
            {
                var batch = tracksToBackfill.Skip(i).Take(batchSize)
                    .Where(t => t.TrackId.HasValue)
                    .ToList();

                if (batch.Count == 0)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var track in batch)
                {
                    sb.Append($"UPDATE public.user_tracks SET track_id = {track.TrackId.Value} " +
                              $"WHERE user_track_id = {track.UserTrackId}; ");
                }

                await connection.ExecuteAsync(sb.ToString());
                updated += batch.Count;
            }

            Log.Information("BackfillTrackIds: Updated {updatedCount}/{totalCount} user tracks for user {userId}",
                updated, tracksToBackfill.Count, userId);
        }
        catch (Exception e)
        {
            Log.Error(e, "BackfillTrackIds: Error backfilling track IDs for user {userId}", userId);
        }
    }

    public async Task BackfillInactiveUserIds(int batchSize = 1000)
    {
        const string cacheKey = "backfill-inactive-user-ids-last-user-id";

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var cutoff = DateTime.UtcNow.AddMonths(-3);
        var recentCutoff = DateTime.UtcNow.AddDays(-7);
        var lastProcessedUserId = cache.TryGetValue(cacheKey, out int lastId) ? lastId : 171000;

        const string sql = "SELECT user_id FROM public.users " +
                           "WHERE last_indexed IS NOT NULL " +
                           "AND (last_used IS NULL OR last_used <= @cutoff) " +
                           "AND (last_updated IS NULL OR last_updated <= @recentCutoff) " +
                           "AND user_id > @lastProcessedUserId " +
                           "ORDER BY user_id " +
                           "LIMIT @batchSize";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var userIds = (await connection.QueryAsync<int>(sql, new { cutoff, recentCutoff, lastProcessedUserId, batchSize })).AsList();

        if (userIds.Count == 0)
        {
            Log.Information("BackfillInactiveUserIds: No more inactive users to process, resetting cursor");
            cache.Remove(cacheKey);
            return;
        }

        Log.Information("BackfillInactiveUserIds: Processing batch of {count} inactive users starting from user_id {startId}",
            userIds.Count, userIds[0]);

        foreach (var userId in userIds)
        {
            var userCacheKey = $"backfill-play-ids-{userId}";
            if (cache.TryGetValue(userCacheKey, out _))
            {
                continue;
            }

            cache.Set(userCacheKey, true);

            await BackfillUserArtistIds(userId);
            await BackfillUserAlbumIds(userId);
            await BackfillUserTrackIds(userId);
        }

        cache.Set(cacheKey, userIds[^1], TimeSpan.FromDays(7));

        Log.Information("BackfillInactiveUserIds: Completed batch of {count} inactive users, last user_id: {lastId}",
            userIds.Count, userIds[^1]);
    }
}
