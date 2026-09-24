using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Core;

public class IdBackfillService(IIdResolver idResolver, IOptions<BotSettings> botSettings)
{
    private readonly BotSettings _botSettings = botSettings.Value;

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

            await idResolver.ResolvePlayIds(playsToBackfill);

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

            await idResolver.ResolveArtistIds(artistsToBackfill);

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

            await idResolver.ResolveAlbumIds(albumsToBackfill);

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

            await idResolver.ResolveTrackIds(tracksToBackfill);

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
}
