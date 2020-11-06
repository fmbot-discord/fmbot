using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
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
        private readonly LastfmClient _lastFMClient;

        private static readonly List<string> UserUpdateFailures = new List<string>();

        private readonly string _connectionString;

        private readonly IMemoryCache _cache;

        public GlobalUpdateService(IConfigurationRoot configuration, IMemoryCache cache)
        {
            this._cache = cache;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
            this._lastFMClient = new LastfmClient(configuration.GetSection("LastFm:Key").Value, configuration.GetSection("LastFm:Secret").Value);
        }


        public async Task<int> UpdateUser(User user)
        {
            Thread.Sleep(1000);

            Log.Information($"Updating {user.UserNameLastFM}");

            if (UserUpdateFailures.Contains(user.UserNameLastFM))
            {
                Log.Information($"Skipped user {user.UserNameLastFM} in updating process");
                return 0;
            }

            var lastPlay = await GetLastStoredPlay(user);
            var recentTracks = await this._lastFMClient.User.GetRecentScrobbles(
                user.UserNameLastFM,
                count: 1000,
                from: lastPlay?.TimePlayed ?? DateTime.UtcNow.AddDays(-14));

            Statistics.LastfmApiCalls.Inc();

            if (!recentTracks.Success || !recentTracks.Content.Any())
            {
                Log.Information($"Something went wrong getting recent tracks for {user.UserNameLastFM} | {recentTracks.Status}");
                if (recentTracks.Success)
                {
                    return 0;
                }

                Log.Information($"Added {user.UserNameLastFM} to update failure list");
                UserUpdateFailures.Add(user.UserNameLastFM);
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
                Log.Information($"No new scrobbles for {user.UserNameLastFM}");
                await SetUserUpdateTime(user.UserId, DateTime.UtcNow, connection);
                return 0;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();

            await UpdateArtistsForUser(user, newScrobbles, cachedArtistAliases, connection);

            await UpdateAlbumsForUser(user, newScrobbles, cachedArtistAliases, connection);

            await UpdateTracksForUser(user, newScrobbles, cachedArtistAliases, connection);

            var latestScrobbleDate = GetLatestScrobbleDate(newScrobbles);
            await SetUserUpdateAndScrobbleTime(user.UserId, DateTime.UtcNow, latestScrobbleDate, connection);

            await connection.CloseAsync();
            return newScrobbles.Count;
        }

        private async Task<IReadOnlyList<ArtistAlias>> GetCachedArtistAliases()
        {
            if (this._cache.TryGetValue("artists", out IReadOnlyList<ArtistAlias> artists))
            {
                return artists;
            }

            await using var db = new FMBotDbContext(this._connectionString);
            artists = await db.ArtistAliases
                .Include(i => i.Artist)
                .ToListAsync();

            this._cache.Set("artists", artists, TimeSpan.FromHours(2));
            Log.Information($"Added {artists.Count} artists to memory cache");

            return artists;
        }

        private async Task<UserPlay> GetLastStoredPlay(User user)
        {
            await using var db = new FMBotDbContext(this._connectionString);
            return await db.UserPlays
                .OrderByDescending(o => o.TimePlayed)
                .FirstOrDefaultAsync(f => f.UserId == user.UserId);
        }


        private async Task UpdatePlaysForUser(User user, IEnumerable<LastTrack> newScrobbles,
            NpgsqlConnection connection)
        {
            Log.Information($"Updating plays for user {user.UserId}");

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
            foreach (var artist in newScrobbles.GroupBy(g => g.ArtistName))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == artist.Key.ToLower());

                var artistName = alias != null ? alias.Artist.Name : artist.Key;


                await using var db = new FMBotDbContext(this._connectionString);
                if (await db.UserArtists.AnyAsync(a => a.UserId == user.UserId &&
                                                       EF.Functions.ILike(a.Name, artistName.ToLower())))
                {
                    await using var updateUserArtist =
                        new NpgsqlCommand(
                            "UPDATE public.user_artists SET playcount = playcount + @playcountToAdd " +
                            "WHERE user_id = @userId AND name ILIKE @name;",
                            connection);

                    updateUserArtist.Parameters.AddWithValue("playcountToAdd", artist.Count());
                    updateUserArtist.Parameters.AddWithValue("userId", user.UserId);
                    updateUserArtist.Parameters.AddWithValue("name", artistName);

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

            Log.Information($"Updated artists for {user.UserNameLastFM}");
        }

        private async Task UpdateAlbumsForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            foreach (var album in newScrobbles.GroupBy(x => new { x.ArtistName, x.AlbumName }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == album.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : album.Key.ArtistName;

                await using var db = new FMBotDbContext(this._connectionString);
                if (await db.UserAlbums.AnyAsync(a => a.UserId == user.UserId &&
                                                      EF.Functions.ILike(a.Name, album.Key.AlbumName) &&
                                                      EF.Functions.ILike(a.ArtistName, album.Key.ArtistName)))
                {
                    await using var updateUserAlbum =
                        new NpgsqlCommand(
                            "UPDATE public.user_albums SET playcount = playcount + @playcountToAdd " +
                            "WHERE user_id = @userId AND name ILIKE @name AND artist_name ILIKE @artistName ;",
                            connection);

                    updateUserAlbum.Parameters.AddWithValue("playcountToAdd", album.Count());
                    updateUserAlbum.Parameters.AddWithValue("userId", user.UserId);
                    updateUserAlbum.Parameters.AddWithValue("name", album.Key.AlbumName);
                    updateUserAlbum.Parameters.AddWithValue("artistName", artistName);

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

            Log.Information($"Updated albums for {user.UserNameLastFM}");
        }

        private async Task UpdateTracksForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases, NpgsqlConnection connection)
        {
            foreach (var track in newScrobbles.GroupBy(x => new { x.ArtistName, x.Name }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == track.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : track.Key.ArtistName;

                await using var db = new FMBotDbContext(this._connectionString);
                if (await db.UserTracks.AnyAsync(a => a.UserId == user.UserId &&
                                                      EF.Functions.ILike(a.Name, track.Key.Name) &&
                                                      EF.Functions.ILike(a.ArtistName, track.Key.ArtistName)))
                {
                    await using var updateUserTrack =
                        new NpgsqlCommand(
                            "UPDATE public.user_tracks SET playcount = playcount + @playcountToAdd " +
                            "WHERE user_id = @userId AND name ILIKE @name AND artist_name ILIKE @artistName;",
                            connection);

                    updateUserTrack.Parameters.AddWithValue("playcountToAdd", track.Count());
                    updateUserTrack.Parameters.AddWithValue("userId", user.UserId);
                    updateUserTrack.Parameters.AddWithValue("name", track.Key.Name);
                    updateUserTrack.Parameters.AddWithValue("artistName", artistName);

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

            Log.Information($"Updated tracks for {user.UserNameLastFM}");
        }

        private async Task SetUserUpdateAndScrobbleTime(int userId, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
        {
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task SetUserUpdateTime(int userId, DateTime now, NpgsqlConnection connection)
        {
            await using var setUpdateTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}' WHERE user_id = {userId};", connection);
            await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private DateTime GetLatestScrobbleDate(List<LastTrack> newScrobbles)
        {
            if (!newScrobbles.Any(a => a.TimePlayed.HasValue))
            {
                Log.Information("No recent scrobble date in update!");
                return DateTime.UtcNow;
            }

            return newScrobbles.First(f => f.TimePlayed.HasValue).TimePlayed.Value.DateTime;
        }

        public static double ConvertToUnixTimestamp(DateTime date)
        {
            var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var diff = date.ToUniversalTime() - origin;
            return Math.Floor(diff.TotalSeconds);
        }
    }
}
