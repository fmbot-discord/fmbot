using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
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

namespace FMBot.LastFM.Repositories
{
    public class UpdateRepository

    {
        private readonly LastFmRepository _lastFmRepository;

        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private readonly string _connectionString;

        private readonly IMemoryCache _cache;

        public UpdateRepository(IConfiguration configuration, IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, LastFmRepository lastFmRepository)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
            this._lastFmRepository = lastFmRepository;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
        }

        public async Task<Response<RecentTrackList>> UpdateUser(UpdateUserQueueItem queueItem)
        {
            Thread.Sleep(queueItem.TimeoutMs);

            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FindAsync(queueItem.UserId);

            Log.Information("Update: Started on {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

            string sessionKey = null;
            if (!string.IsNullOrEmpty(user.SessionKeyLastFm))
            {
                sessionKey = user.SessionKeyLastFm;
            }

            var lastStoredPlay = await GetLastStoredPlay(user);

            var dateAgo = lastStoredPlay?.TimePlayed.AddMinutes(-10) ?? DateTime.UtcNow.AddDays(-14);
            var timeFrom = (long?)((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

            var count = 900;
            var totalPlaycountCorrect = false;
            var now = DateTime.UtcNow;
            if (dateAgo > now.AddHours(-1) && dateAgo < now.AddMinutes(-30))
            {
                count = 60;
                timeFrom = null;
                totalPlaycountCorrect = true;
            }
            else if (dateAgo > now.AddHours(-2) && dateAgo < now.AddMinutes(-30))
            {
                count = 120;
                timeFrom = null;
                totalPlaycountCorrect = true;
            }
            else if (dateAgo > now.AddHours(-12) && dateAgo < now.AddMinutes(-30))
            {
                count = 500;
                timeFrom = null;
                totalPlaycountCorrect = true;
            }

            var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(
                user.UserNameLastFM,
                count,
                true,
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
                .Where(w => !w.NowPlaying &&
                            w.TimePlayed != null &&
                            (lastStoredPlay?.TimePlayed == null || w.TimePlayed > lastStoredPlay.TimePlayed))
                .ToList();

            if (!newScrobbles.Any())
            {
                Log.Information("Update: After local filter no new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);

                if (!user.TotalPlaycount.HasValue)
                {
                    recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, newScrobbles.Count, connection, totalPlaycountCorrect ? recentTracks.Content.TotalAmount : null);
                }
                else if (totalPlaycountCorrect)
                {
                    await SetOrUpdateUserPlaycount(user, newScrobbles.Count, connection, recentTracks.Content.TotalAmount);
                }
                else
                {
                    recentTracks.Content.TotalAmount = user.TotalPlaycount.Value;
                }
                recentTracks.Content.NewRecentTracksAmount = 0;
                return recentTracks;
            }

            await CacheArtistAliases();

            try
            {
                var cacheKey = $"{user.UserId}-update-in-progress";
                if (this._cache.TryGetValue(cacheKey, out bool _))
                {
                    return recentTracks;
                }

                this._cache.Set(cacheKey, true, TimeSpan.FromSeconds(1));

                recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, newScrobbles.Count, connection, totalPlaycountCorrect ? recentTracks.Content.TotalAmount : null);

                await UpdatePlaysForUser(user, newScrobbles, connection);

                var userArtists = await GetUserArtists(user.UserId, connection);
                var userAlbums = await GetUserAlbums(user.UserId, connection);
                var userTracks = await GetUserTracks(user.UserId, connection);

                await UpdateArtistsForUser(user, newScrobbles, connection, userArtists);
                await UpdateAlbumsForUser(user, newScrobbles, connection, userAlbums);
                await UpdateTracksForUser(user, newScrobbles, connection, userTracks);

                var lastNewScrobble = newScrobbles.OrderByDescending(o => o.TimePlayed).FirstOrDefault();
                if (lastNewScrobble?.TimePlayed != null)
                {
                    await SetUserLastScrobbleTime(user, lastNewScrobble.TimePlayed.Value, connection);
                }

                await SetUserUpdateTime(user, DateTime.UtcNow, connection);

                this._cache.Remove($"user-{user.UserId}-topartists-alltime");
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

        private async Task CacheArtistAliases()
        {
            const string cacheKey = "artist-aliases";
            var cacheTime = TimeSpan.FromMinutes(5);

            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var artistAliases = await db.ArtistAliases
                .Include(i => i.Artist)
                .ToListAsync();

            foreach (var alias in artistAliases)
            {
                this._cache.Set(CacheKeyForAlias(alias.Alias.ToLower()), alias.Artist.Name.ToLower(), cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
            Log.Information($"Added {artistAliases.Count} artist aliases to memory cache");
        }

        private static string CacheKeyForAlias(string aliasName)
        {
            return $"artist-alias-{aliasName}";
        }

        private async Task<UserPlay> GetLastStoredPlay(User user)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .OrderByDescending(o => o.TimePlayed)
                .FirstOrDefaultAsync(f => f.UserId == user.UserId);
        }

        private async Task UpdatePlaysForUser(User user,
            IEnumerable<RecentTrack> newScrobbles,
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

        private async Task<IReadOnlyCollection<UserArtist>> GetUserArtists(int userId, NpgsqlConnection connection)
        {
            const string sql = "SELECT * FROM public.user_artists where user_id = @userId";
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return (await connection.QueryAsync<UserArtist>(sql, new
            {
                userId
            })).ToList();
        }

        private async Task UpdateArtistsForUser(User user,
            IEnumerable<RecentTrack> newScrobbles,
            NpgsqlConnection connection,
            IReadOnlyCollection<UserArtist> userArtists)
        {
            await using var db = this._contextFactory.CreateDbContext();

            foreach (var artist in newScrobbles.GroupBy(g => g.ArtistName.ToLower()))
            {
                var alias = (string)this._cache.Get(CacheKeyForAlias(artist.Key.ToLower()));

                var artistName = alias ?? artist.Key;

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

        private async Task<IReadOnlyCollection<UserAlbum>> GetUserAlbums(int userId, NpgsqlConnection connection)
        {
            const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return (await connection.QueryAsync<UserAlbum>(sql, new
            {
                userId
            })).ToList();
        }

        private async Task UpdateAlbumsForUser(User user,
            IEnumerable<RecentTrack> newScrobbles,
            NpgsqlConnection connection,
            IReadOnlyCollection<UserAlbum> userAlbums)
        {
            await using var db = this._contextFactory.CreateDbContext();


            foreach (var album in newScrobbles
                .Where(w => w.AlbumName != null)
                .GroupBy(x => new
                {
                    ArtistName = x.ArtistName.ToLower(),
                    AlbumName = x.AlbumName.ToLower()
                }))
            {
               var alias = (string)this._cache.Get(CacheKeyForAlias(album.Key.ArtistName.ToLower()));

                var artistName = alias ?? album.Key.ArtistName;

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

        private async Task<IReadOnlyCollection<UserTrack>> GetUserTracks(int userId, NpgsqlConnection connection)
        {
            const string sql = "SELECT * FROM public.user_tracks where user_id = @userId";
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return (await connection.QueryAsync<UserTrack>(sql, new
            {
                userId
            })).ToList();
        }

        private async Task UpdateTracksForUser(User user,
            IEnumerable<RecentTrack> newScrobbles,
            NpgsqlConnection connection,
            IReadOnlyCollection<UserTrack> userTracks)
        {
            await using var db = this._contextFactory.CreateDbContext();

            foreach (var track in newScrobbles.GroupBy(x => new
            {
                ArtistName = x.ArtistName.ToLower(),
                TrackName = x.TrackName.ToLower()
            }))
            {
                var alias = (string)this._cache.Get(CacheKeyForAlias(track.Key.ArtistName.ToLower()));

                var artistName = alias ?? track.Key.ArtistName;

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

        private async Task SetUserLastScrobbleTime(User user, DateTime lastScrobble, NpgsqlConnection connection)
        {
            user.LastScrobbleUpdate = lastScrobble;
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {user.UserId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task SetUserUpdateTime(User user, DateTime updateTime, NpgsqlConnection connection)
        {
            user.LastUpdated = updateTime;
            await using var setUpdateTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{updateTime:u}' WHERE user_id = {user.UserId};", connection);
            await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task<long> SetOrUpdateUserPlaycount(User user, long playcountToAdd, NpgsqlConnection connection, long? correctPlaycount = null)
        {
            if (!correctPlaycount.HasValue)
            {
                if (!user.TotalPlaycount.HasValue)
                {
                    var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(
                        user.UserNameLastFM,
                        count: 1,
                        useCache: false,
                        user.SessionKeyLastFm);

                    await using var setPlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {recentTracks.Content.TotalAmount} WHERE user_id = {user.UserId};", connection);
                    await setPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

                    user.TotalPlaycount = recentTracks.Content.TotalAmount;

                    return recentTracks.Content.TotalAmount;
                }

                var updatedPlaycount = user.TotalPlaycount.Value + playcountToAdd;
                await using var updatePlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {updatedPlaycount} WHERE user_id = {user.UserId};", connection);
                await updatePlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

                return updatedPlaycount;
            }

            await using var updateCorrectPlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {correctPlaycount} WHERE user_id = {user.UserId};", connection);
            await updateCorrectPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

            return correctPlaycount.Value;
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

        public async Task CorrectUserArtistPlaycount(int userId, string artistName, long correctPlaycount)
        {
            if (correctPlaycount < 20)
            {
                return;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .Include(i => i.Artists)
                .FirstOrDefaultAsync(f => f.UserId == userId);

            if (user?.LastUpdated == null || !user.Artists.Any() || user.LastUpdated < DateTime.UtcNow.AddMinutes(-1))
            {
                return;
            }

            var userArtist = user.Artists.FirstOrDefault(f => f.Name.ToLower() == artistName.ToLower());

            if (userArtist == null ||
                userArtist.Playcount < 20 ||
                userArtist.Playcount > (correctPlaycount - 4) && userArtist.Playcount < (correctPlaycount + 4))
            {
                return;
            }


            Log.Information("Corrected playcount for user {userId} | {lastFmUserName} for artist {artistName} from {oldPlaycount} to {newPlaycount}",
                user.UserId, user.UserNameLastFM, userArtist.Name, userArtist.Playcount, correctPlaycount);

            userArtist.Playcount = (int)correctPlaycount;

            db.Entry(userArtist).State = EntityState.Modified;

            await db.SaveChangesAsync();
        }
    }
}
