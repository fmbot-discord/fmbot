using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Models;
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

        private static readonly List<string> UserUpdateFailures = new List<string>();

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

            string sessionKey = null;
            if (!string.IsNullOrEmpty(user.SessionKeyLastFm))
            {
                sessionKey = user.SessionKeyLastFm;
            }

            var dateAgo = lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-14);
            var timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

            var recentTracks = await this._lastFmService.GetRecentTracksAsync(
                user.UserNameLastFM,
                count: 1000,
                useCache: true,
                sessionKey,
                timeFrom);

            Statistics.LastfmApiCalls.Inc();

            if (!recentTracks.Success)
            {
                Log.Information("Update: Something went wrong getting tracks for {userId} | {userNameLastFm} | {responseStatus}", user.UserId, user.UserNameLastFM, recentTracks.Error);

                if ((user.LastUsed == null || user.LastUsed < DateTime.UtcNow.AddDays(-31)) && recentTracks.Error != ResponseStatus.Failure)
                {
                    UserUpdateFailures.Add(user.UserNameLastFM);
                    Log.Information($"Added {user.UserNameLastFM} to update failure list");
                }

                return 0;
            }

            await using var connection = new NpgsqlConnection(this._connectionString);
            await connection.OpenAsync();

            if (!recentTracks.Content.RecentTracks.Track.Any())
            {
                Log.Information("Update: No new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);
                return 0;
            }

            AddRecentPlayToMemoryCache(user.UserId, recentTracks.Content.RecentTracks.Track.First());

            var newScrobbles = recentTracks.Content.RecentTracks.Track
                .Where(w => (w.Attr == null || !w.Attr.Nowplaying) &&
                            w.Date != null &&
                            DateTime.UnixEpoch.AddSeconds(w.Date.Uts).ToUniversalTime() > user.LastScrobbleUpdate)
                .ToList();

            if (!newScrobbles.Any())
            {
                Log.Information("Update: After local filter no new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);
                return 0;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();

            try
            {
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
                .Where(w => (w.Attr == null || !w.Attr.Nowplaying) &&
                            w.Date != null &&
                            DateTime.UnixEpoch.AddSeconds(w.Date.Uts).ToUniversalTime() > (lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-Constants.DaysToStorePlays).Date))
                .Select(s => new UserPlay
                {
                    ArtistName = s.Artist.Text,
                    AlbumName = !string.IsNullOrWhiteSpace(s.Album?.Text) ? s.Album.Text : null,
                    TrackName = s.Name,
                    TimePlayed = DateTime.UnixEpoch.AddSeconds(s.Date.Uts).ToUniversalTime(),
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

            foreach (var artist in newScrobbles.GroupBy(g => g.Artist.Text))
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
                .Where(w => !string.IsNullOrWhiteSpace(w.Album?.Text))
                .GroupBy(x => new { ArtistName = x.Artist.Text, AlbumName = x.Album.Text }))
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
                    Log.Information($"Updated album {album.Key.AlbumName} for {user.UserNameLastFM}");
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
                    Log.Information($"Added album {album.Key.AlbumName} for {user.UserNameLastFM}");
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

            foreach (var track in newScrobbles.GroupBy(x => new { ArtistName = x.Artist.Text, x.Name }))
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

#if DEBUG
                    Log.Information($"Updated track {track.Key.Name} for {user.UserNameLastFM}");
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
                    addUserTrack.Parameters.AddWithValue("trackName", track.Key.Name);
                    addUserTrack.Parameters.AddWithValue("artistName", artistName);
                    addUserTrack.Parameters.AddWithValue("trackPlaycount", track.Count());

#if DEBUG
                    Log.Information($"Added track {track.Key.Name} for {user.UserNameLastFM}");
#endif

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

        private DateTime GetLatestScrobbleDate(User user, List<RecentTrack> newScrobbles)
        {
            var scrobbleWithDate = newScrobbles
                .FirstOrDefault(f => f.Date != null && (f.Attr == null || !f.Attr.Nowplaying));

            if (scrobbleWithDate == null)
            {
                Log.Information("Update: No recent scrobble date found for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return DateTime.UtcNow;
            }

            return DateTime.UnixEpoch.AddSeconds(scrobbleWithDate.Date.Uts).ToUniversalTime();
        }

        private void AddRecentPlayToMemoryCache(int userId, RecentTrack track) 
        {
            if (track.Attr != null && track.Attr.Nowplaying || track.Date != null &&
                DateTime.UnixEpoch.AddSeconds(track.Date.Uts).ToUniversalTime() > DateTime.UtcNow.AddMinutes(-8))
            {
                var userPlay = new UserPlay
                {
                    ArtistName = track.Artist.Text.ToLower(),
                    AlbumName = !string.IsNullOrWhiteSpace(track.Album?.Text) ? track.Album.Text.ToLower() : null,
                    TrackName = track.Name.ToLower(),
                    UserId = userId,
                    TimePlayed = track.Date != null ? DateTime.UnixEpoch.AddSeconds(track.Date.Uts).ToUniversalTime() : DateTime.UtcNow
                };

                this._cache.Set($"{userId}-last-play", userPlay, TimeSpan.FromMinutes(15));
            }
        }
    }
}
