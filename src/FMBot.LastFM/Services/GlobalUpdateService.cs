using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.LastFM.Services
{
    public class GlobalUpdateService

    {
        private readonly LastfmClient _lastFmClient;

        private static readonly List<string> UserUpdateFailures = new List<string>();

        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private readonly string _connectionString;

        private readonly IMemoryCache _cache;

        public GlobalUpdateService(IConfigurationRoot configuration, IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
            this._lastFmClient = new LastfmClient(configuration.GetSection("LastFm:Key").Value, configuration.GetSection("LastFm:Secret").Value);
        }

        public async Task<int> UpdateUser(UpdateUserQueueItem queueItem)
        {
            Thread.Sleep(queueItem.TimeoutMs);

            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FindAsync(queueItem.UserId);

            Log.Information("Update: Started on {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

            if (UserUpdateFailures.Contains(user.UserNameLastFM))
            {
                Log.Information("Update: Skipped {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return 0;
            }

            var lastPlay = await GetLastStoredPlay(user);
            var recentTracks = await this._lastFmClient.User.GetRecentScrobbles(
                user.UserNameLastFM,
                count: 1000,
                from: lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-14));

            Statistics.LastfmApiCalls.Inc();

            if (!recentTracks.Success)
            {
                Log.Information("Update: Something went wrong getting tracks for {userId} | {userNameLastFm} | {responseStatus}", user.UserId, user.UserNameLastFM, recentTracks.Status);
                UserUpdateFailures.Add(user.UserNameLastFM);

                Log.Information($"Added {user.UserNameLastFM} to update failure list");
                return 0;
            }
            if(!recentTracks.Content.Any())
            {
                Log.Information("Update: No new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return 0;
            }

            var newScrobbles = recentTracks.Content
                .Where(w => w.TimePlayed.HasValue && w.TimePlayed.Value.DateTime > user.LastScrobbleUpdate)
                .ToList();

            await using var connection = new NpgsqlConnection(this._connectionString);
            await connection.OpenAsync();

            await UpdatePlaysForUser(user, recentTracks, connection);

            if (!newScrobbles.Any())
            {
                Log.Information("Update: After local filter no new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);
                return 0;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();

            try
            {
                await UpdateArtistsForUser(user, newScrobbles, cachedArtistAliases, connection);

                await UpdateAlbumsForUser(user, newScrobbles, cachedArtistAliases, connection);

                await UpdateTracksForUser(user, newScrobbles, cachedArtistAliases, connection);

                var latestScrobbleDate = GetLatestScrobbleDate(user, newScrobbles);
                await SetUserUpdateAndScrobbleTime(user, DateTime.UtcNow, latestScrobbleDate, connection);
            }
            catch (Exception e)
            {
                Log.Error(e, "Update: Error in update process for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
            }

            await connection.CloseAsync();
            return newScrobbles.Count;
        }

        private async Task<IReadOnlyList<ArtistAlias>> GetCachedArtistAliases()
        {
            if (this._cache.TryGetValue("artists", out IReadOnlyList<ArtistAlias> artists))
            {
                return artists;
            }

            await using var db = this._contextFactory.CreateDbContext();
            artists = await db.ArtistAliases
                .Include(i => i.Artist)
                .ToListAsync();

            this._cache.Set("artists", artists, TimeSpan.FromHours(2));
            Log.Information($"Added {artists.Count} artists to memory cache");

            return artists;
        }

        private async Task<UserPlay> GetLastStoredPlay(User user)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .OrderByDescending(o => o.TimePlayed)
                .FirstOrDefaultAsync(f => f.UserId == user.UserId);
        }


        private async Task UpdatePlaysForUser(User user, IEnumerable<LastTrack> newScrobbles,
            NpgsqlConnection connection)
        {
            Log.Verbose("Update: Updating plays for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

            await using var deleteOldPlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                               "WHERE user_id = @userId AND time_played < @playExpirationDate;", connection);

            deleteOldPlays.Parameters.AddWithValue("userId", user.UserId);
            deleteOldPlays.Parameters.AddWithValue("playExpirationDate", DateTime.UtcNow.AddDays(-Constants.DaysToStorePlays));

            await deleteOldPlays.ExecuteNonQueryAsync();

            var lastPlay = await GetLastStoredPlay(user);

            var userPlays = newScrobbles
                .Where(w => w.TimePlayed.HasValue &&
                            w.TimePlayed.Value.DateTime > (lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-Constants.DaysToStorePlays).Date))
                .Select(s => new UserPlay
                {
                    TrackName = s.Name,
                    AlbumName = s.AlbumName,
                    ArtistName = s.ArtistName,
                    TimePlayed = s.TimePlayed.Value.DateTime,
                    UserId = user.UserId
                }).ToList();

            var copyHelper = new PostgreSQLCopyHelper<UserPlay>("public", "user_plays")
                .MapText("track_name", x => x.TrackName)
                .MapText("album_name", x => x.AlbumName)
                .MapText("artist_name", x => x.ArtistName)
                .MapTimeStamp("time_played", x => x.TimePlayed)
                .MapInteger("user_id", x => x.UserId);

            await copyHelper.SaveAllAsync(connection, userPlays);
        }

        private async Task UpdateArtistsForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var userArtists = await db.UserArtists
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();

            foreach (var artist in newScrobbles.GroupBy(g => g.ArtistName))
            {
                var alias = cachedArtistAliases
                            .FirstOrDefault(f => f.Alias.ToLower() == artist.Key.ToLower());

                var artistName = alias != null ? alias.Artist.Name : artist.Key;

                var existingUserArtist =
                    userArtists.FirstOrDefault(a => a.Name.ToLower() == artistName.ToLower());

                if (existingUserArtist != null)
                {
                    await using var updateUserArtist =
                        new NpgsqlCommand(
                            "UPDATE public.user_artists SET playcount = @newPlaycount " +
                            "WHERE user_artist_id = @userArtistId;",
                            connection);

                    updateUserArtist.Parameters.AddWithValue("newPlaycount", existingUserArtist.Playcount + artist.Count());
                    updateUserArtist.Parameters.AddWithValue("userArtistId", existingUserArtist.UserArtistId);

                    //Log.Information($"Updated artist {artistName} for {user.UserNameLastFM}");
                    await updateUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    await using var addUserArtist =
                        new NpgsqlCommand("INSERT INTO public.user_artists(user_id, name, playcount)" +
                                          "VALUES(@userId, @artistName, @artistPlaycount); ",
                            connection);

                    addUserArtist.Parameters.AddWithValue("userId", user.UserId);
                    addUserArtist.Parameters.AddWithValue("artistName", artistName);
                    addUserArtist.Parameters.AddWithValue("artistPlaycount", artist.Count());

                    //Log.Information($"Added artist {artistName} for {user.UserNameLastFM}");
                    await addUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            Log.Verbose("Update: Updated artists for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

        }

        private async Task UpdateAlbumsForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var userAlbums = await db.UserAlbums
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();

            foreach (var album in newScrobbles.GroupBy(x => new { x.ArtistName, x.AlbumName }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == album.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : album.Key.ArtistName;

                var existingUserAlbum =
                    userAlbums.FirstOrDefault(a => a.Name.ToLower() == album.Key.AlbumName.ToLower() &&
                                                   a.ArtistName.ToLower() == album.Key.ArtistName.ToLower());

                if (existingUserAlbum != null)
                {
                    await using var updateUserAlbum =
                        new NpgsqlCommand(
                            "UPDATE public.user_albums SET playcount = @newPlaycount " +
                            "WHERE user_album_id = @userAlbumId;",
                            connection);

                    updateUserAlbum.Parameters.AddWithValue("newPlaycount", existingUserAlbum.Playcount + album.Count());
                    updateUserAlbum.Parameters.AddWithValue("userAlbumId", existingUserAlbum.UserAlbumId);

                    //Log.Information($"Updated album {album.Key.AlbumName} for {user.UserNameLastFM}");
                    await updateUserAlbum.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    await using var addUserAlbum =
                        new NpgsqlCommand("INSERT INTO public.user_albums(user_id, name, artist_name, playcount)" +
                                          "VALUES(@userId, @albumName, @artistName, @albumPlaycount); ",
                            connection);

                    addUserAlbum.Parameters.AddWithValue("userId", user.UserId);
                    addUserAlbum.Parameters.AddWithValue("albumName", album.Key.AlbumName);
                    addUserAlbum.Parameters.AddWithValue("artistName", artistName);
                    addUserAlbum.Parameters.AddWithValue("albumPlaycount", album.Count());

                    //Log.Information($"Added album {album.Key.AlbumName} for {user.UserNameLastFM}");
                    await addUserAlbum.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            Log.Verbose("Update: Updated albums for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
        }

        private async Task UpdateTracksForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var userTracks = await db.UserTracks
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();

            foreach (var track in newScrobbles.GroupBy(x => new { x.ArtistName, x.Name }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == track.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : track.Key.ArtistName;

                var existingUserTrack =
                    userTracks.FirstOrDefault(a => a.Name.ToLower() == track.Key.Name.ToLower() &&
                                                   a.ArtistName.ToLower() == track.Key.ArtistName.ToLower());

                if (existingUserTrack != null)
                {
                    await using var updateUserTrack =
                        new NpgsqlCommand(
                            "UPDATE public.user_tracks SET playcount = @newPlaycount " +
                            "WHERE user_track_id = @userTrackId",
                            connection);

                    updateUserTrack.Parameters.AddWithValue("newPlaycount", existingUserTrack.Playcount + track.Count());
                    updateUserTrack.Parameters.AddWithValue("userTrackId", existingUserTrack.UserTrackId);

                    //Log.Information($"Updated track {track.Key.Name} for {user.UserNameLastFM}");
                    await updateUserTrack.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    await using var addUserTrack =
                        new NpgsqlCommand("INSERT INTO public.user_tracks(user_id, name, artist_name, playcount)" +
                                          "VALUES(@userId, @trackName, @artistName, @trackPlaycount); ",
                            connection);

                    addUserTrack.Parameters.AddWithValue("userId", user.UserId);
                    addUserTrack.Parameters.AddWithValue("trackName", track.Key.Name);
                    addUserTrack.Parameters.AddWithValue("artistName", artistName);
                    addUserTrack.Parameters.AddWithValue("trackPlaycount", track.Count());

                    //Log.Information($"Added track {track.Key.Name} for {user.UserNameLastFM}");
                    await addUserTrack.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            Log.Verbose("Update: Updated tracks for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
        }

        private async Task SetUserUpdateAndScrobbleTime(User user, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
        {
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {user.UserId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task SetUserUpdateTime(User user, DateTime now, NpgsqlConnection connection)
        {
            await using var setUpdateTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}' WHERE user_id = {user.UserId};", connection);
            await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private DateTime GetLatestScrobbleDate(User user, List<LastTrack> newScrobbles)
        {
            if (!newScrobbles.Any(a => a.TimePlayed.HasValue))
            {
                Log.Information("Update: No recent scrobble date found for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return DateTime.UtcNow;
            }

            return newScrobbles.First(f => f.TimePlayed.HasValue).TimePlayed.Value.DateTime;
        }
    }
}
