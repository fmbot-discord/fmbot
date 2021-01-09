using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
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
        private readonly LastFmService _lastFmService;

        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private readonly string _connectionString;

        private readonly IMemoryCache _cache;

        public GlobalUpdateService(IConfigurationRoot configuration, IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
        }

        public async Task<Response<RecentTrackList>> UpdateUser(UpdateUserQueueItem queueItem)
        {
            Thread.Sleep(queueItem.TimeoutMs);

            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FindAsync(queueItem.UserId);

            Log.Information("Update: Started on {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

            var lastPlay = await GetLastStoredPlay(user);

            string sessionKey = null;
            if (!string.IsNullOrEmpty(user.SessionKeyLastFm))
            {
                sessionKey = user.SessionKeyLastFm;
            }

            var dateAgo = lastPlay?.TimePlayed.AddMinutes(-30) ?? DateTime.UtcNow.AddDays(-14);
            var timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

            var recentTracks = await this._lastFmService.GetRecentTracksAsync(
                user.UserNameLastFM,
                count: 1000,
                useCache: true,
                sessionKey,
                timeFrom);

            if (!recentTracks.Success)
            {
                Log.Information("Update: Something went wrong getting tracks for {userId} | {userNameLastFm} | {responseStatus}", user.UserId, user.UserNameLastFM, recentTracks.Error);

                if (recentTracks.Error == ResponseStatus.MissingParameters)
                {
                    await AddOrUpdateInactiveUserMissingParameterError(user);
                }
                if (recentTracks.Error == ResponseStatus.LoginRequired)
                {
                    await AddOrUpdatePrivateUserMissingParameterError(user);
                }

                recentTracks.Content = new RecentTrackList
                {
                    NewRecentTracksAmount = 0
                };
                return recentTracks;
            }

            await RemoveInactiveUserIfExists(user);

            await using var connection = new NpgsqlConnection(this._connectionString);
            await connection.OpenAsync();

            if (!recentTracks.Content.RecentTracks.Any())
            {
                Log.Information("Update: No new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);

                recentTracks.Content.NewRecentTracksAmount = 0;
                return recentTracks;
            }

            AddRecentPlayToMemoryCache(user.UserId, recentTracks.Content.RecentTracks.First());

            var newScrobbles = recentTracks.Content.RecentTracks
                .Where(w => (!w.NowPlaying) &&
                            w.TimePlayed != null &&
                            w.TimePlayed > user.LastScrobbleUpdate)
                .ToList();

            if (!newScrobbles.Any())
            {
                Log.Information("Update: After local filter no new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);

                if (!user.TotalPlaycount.HasValue)
                {
                    recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, newScrobbles.Count, connection);
                }
                else
                {
                    recentTracks.Content.TotalAmount = user.TotalPlaycount.Value;
                }
                recentTracks.Content.NewRecentTracksAmount = 0;
                return recentTracks;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();

            try
            {
                recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, newScrobbles.Count, connection);

                await UpdatePlaysForUser(user, newScrobbles, connection);

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

            Statistics.UpdatedUsers.Inc();

            await connection.CloseAsync();

            recentTracks.Content.NewRecentTracksAmount = newScrobbles.Count;
            return recentTracks;
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


        private async Task UpdatePlaysForUser(User user, List<RecentTrack> newScrobbles,
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
                .Where(w => !w.NowPlaying &&
                            w.TimePlayed.HasValue &&
                            w.TimePlayed > (lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-Constants.DaysToStorePlays).Date))
                .Select(s => new UserPlay
                {
                    ArtistName = s.ArtistName,
                    AlbumName = s.AlbumName,
                    TrackName = s.TrackName,
                    TimePlayed = s.TimePlayed.Value,
                    UserId = user.UserId
                }).ToList();

            if (!userPlays.Any())
            {
                return;
            }

            var copyHelper = new PostgreSQLCopyHelper<UserPlay>("public", "user_plays")
                .MapText("track_name", x => x.TrackName)
                .MapText("album_name", x => x.AlbumName)
                .MapText("artist_name", x => x.ArtistName)
                .MapTimeStamp("time_played", x => x.TimePlayed)
                .MapInteger("user_id", x => x.UserId);

            await copyHelper.SaveAllAsync(connection, userPlays);

#if DEBUG
            Log.Information($"Added {userPlays.Count} plays to database for {user.UserNameLastFM}");
#endif
        }

        private async Task UpdateArtistsForUser(User user, List<RecentTrack> newScrobbles,
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

#if DEBUG
                    Log.Information($"Updated artist {artistName} for {user.UserNameLastFM}");
#endif

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

#if DEBUG
                    Log.Information($"Added artist {artistName} for {user.UserNameLastFM}");
#endif
                    await addUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            Log.Verbose("Update: Updated artists for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

        }

        private async Task UpdateAlbumsForUser(User user, List<RecentTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var userAlbums = await db.UserAlbums
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();

            foreach (var album in newScrobbles
                .Where(w => w.AlbumName != null)
                .GroupBy(x => new { x.ArtistName, x.AlbumName }))
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

#if DEBUG
                    Log.Information($"Updated album {album.Key.AlbumName} for {user.UserNameLastFM} (+{album.Count()} plays)");
#endif
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

#if DEBUG
                    Log.Information($"Added album {album.Key.ArtistName} - {album.Key.AlbumName} for {user.UserNameLastFM}");
#endif

                    await addUserAlbum.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            Log.Verbose("Update: Updated albums for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
        }

        private async Task UpdateTracksForUser(User user, List<RecentTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var userTracks = await db.UserTracks
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();

            foreach (var track in newScrobbles.GroupBy(x => new { x.ArtistName, x.TrackName }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == track.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : track.Key.ArtistName;

                var existingUserTrack =
                    userTracks.FirstOrDefault(a => a.Name.ToLower() == track.Key.TrackName.ToLower() &&
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

#if DEBUG
                    Log.Information($"Updated track {track.Key.TrackName} for {user.UserNameLastFM} (+{track.Count()} plays)");
#endif

                    await updateUserTrack.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    await using var addUserTrack =
                        new NpgsqlCommand("INSERT INTO public.user_tracks(user_id, name, artist_name, playcount)" +
                                          "VALUES(@userId, @trackName, @artistName, @trackPlaycount); ",
                            connection);

                    addUserTrack.Parameters.AddWithValue("userId", user.UserId);
                    addUserTrack.Parameters.AddWithValue("trackName", track.Key.TrackName);
                    addUserTrack.Parameters.AddWithValue("artistName", artistName);
                    addUserTrack.Parameters.AddWithValue("trackPlaycount", track.Count());

#if DEBUG
                    Log.Information($"Added track {track.Key.ArtistName} - {track.Key.TrackName} for {user.UserNameLastFM}");
#endif

                    await addUserTrack.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task SetUserUpdateAndScrobbleTime(User user, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
        {
            user.LastUpdated = now;
            user.LastScrobbleUpdate = lastScrobble;
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {user.UserId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task SetUserUpdateTime(User user, DateTime now, NpgsqlConnection connection)
        {
            user.LastUpdated = now;
            await using var setUpdateTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}' WHERE user_id = {user.UserId};", connection);
            await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task<long> SetOrUpdateUserPlaycount(User user, long playcountToAdd, NpgsqlConnection connection)
        {
            if (!user.TotalPlaycount.HasValue)
            {
                var recentTracks = await this._lastFmService.GetRecentTracksAsync(
                    user.UserNameLastFM,
                    count: 1,
                    useCache: false,
                    user.SessionKeyLastFm);

                await using var setPlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {recentTracks.Content.TotalAmount} WHERE user_id = {user.UserId};", connection);
                await setPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

                user.TotalPlaycount = recentTracks.Content.TotalAmount;

                return recentTracks.Content.TotalAmount;
            }
            else
            {
                var updatedPlaycount = user.TotalPlaycount.Value + playcountToAdd;
                await using var updatePlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {updatedPlaycount} WHERE user_id = {user.UserId};", connection);
                await updatePlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

                return updatedPlaycount;
            }
        }

        private DateTime GetLatestScrobbleDate(User user, List<RecentTrack> newScrobbles)
        {
            var scrobbleWithDate = newScrobbles
                .FirstOrDefault(f => f.TimePlayed.HasValue && !f.NowPlaying);

            if (scrobbleWithDate == null)
            {
                Log.Information("Update: No recent scrobble date found for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return DateTime.UtcNow;
            }

            return scrobbleWithDate.TimePlayed.Value;
        }

        private void AddRecentPlayToMemoryCache(int userId, RecentTrack track)
        {
            if (track.NowPlaying || track.TimePlayed != null && track.TimePlayed > DateTime.UtcNow.AddMinutes(-8))
            {
                var userPlay = new UserPlay
                {
                    ArtistName = track.ArtistName,
                    AlbumName = track.AlbumName,
                    TrackName = track.TrackName,
                    UserId = userId,
                    TimePlayed = track.TimePlayed ?? DateTime.UtcNow
                };

                this._cache.Set($"{userId}-last-play", userPlay, TimeSpan.FromMinutes(15));
            }
        }

        private async Task AddOrUpdateInactiveUserMissingParameterError(User user)
        {
            if (user.LastUsed > DateTime.UtcNow.AddDays(-20) || !string.IsNullOrEmpty(user.SessionKeyLastFm))
            {
                return;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var existingInactiveUser = await db.InactiveUsers.FirstOrDefaultAsync(f => f.UserId == user.UserId);

            if (existingInactiveUser == null)
            {
                var inactiveUser = new InactiveUsers
                {
                    UserNameLastFM = user.UserNameLastFM,
                    UserId = user.UserId,
                    Created = DateTime.UtcNow,
                    Updated = DateTime.UtcNow,
                    MissingParametersErrorCount = 1
                };

                await db.InactiveUsers.AddAsync(inactiveUser);

                Log.Verbose("InactiveUsers: Added user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
            }
            else
            {
                existingInactiveUser.MissingParametersErrorCount++;
                existingInactiveUser.Updated = DateTime.UtcNow;

                db.Entry(existingInactiveUser).State = EntityState.Modified;

                Log.Verbose("InactiveUsers: Updated user {userId} | {userNameLastFm} (missingparameter +1)", user.UserId, user.UserNameLastFM);
            }

            await db.SaveChangesAsync();
        }

        private async Task AddOrUpdatePrivateUserMissingParameterError(User user)
        {
            if (user.LastUsed > DateTime.UtcNow.AddDays(-20) || !string.IsNullOrEmpty(user.SessionKeyLastFm))
            {
                return;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var existingPrivateUser = await db.InactiveUsers.FirstOrDefaultAsync(f => f.UserId == user.UserId);

            if (existingPrivateUser == null)
            {
                var inactiveUser = new InactiveUsers
                {
                    UserNameLastFM = user.UserNameLastFM,
                    UserId = user.UserId,
                    Created = DateTime.UtcNow,
                    Updated = DateTime.UtcNow,
                    RecentTracksPrivateCount = 1
                };

                await db.InactiveUsers.AddAsync(inactiveUser);

                Log.Verbose("InactiveUsers: Added private user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
            }
            else
            {
                existingPrivateUser.RecentTracksPrivateCount++;
                existingPrivateUser.Updated = DateTime.UtcNow;

                db.Entry(existingPrivateUser).State = EntityState.Modified;

                Log.Verbose("InactiveUsers: Updated private user {userId} | {userNameLastFm} (RecentTracksPrivateCount +1)", user.UserId, user.UserNameLastFM);
            }

            await db.SaveChangesAsync();
        }

        private async Task RemoveInactiveUserIfExists(User user)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var existingInactiveUser = await db.InactiveUsers.FirstOrDefaultAsync(f => f.UserId == user.UserId);

            if (existingInactiveUser != null)
            {
                db.InactiveUsers.Remove(existingInactiveUser);

                Log.Verbose("InactiveUsers: Removed user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

                await db.SaveChangesAsync();
            }
        }
    }
}
