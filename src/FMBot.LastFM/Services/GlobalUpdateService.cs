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
using Serilog;

namespace FMBot.LastFM.Services
{
    public class GlobalUpdateService

    {
        private readonly LastfmClient _lastFMClient;

        private readonly string _key;
        private readonly string _secret;

        private readonly string _connectionString;

        private readonly IMemoryCache _cache;

        public GlobalUpdateService(IConfigurationRoot configuration, IMemoryCache cache)
        {
            this._cache = cache;
            this._key = configuration.GetSection("LastFm:Key").Value;
            this._secret = configuration.GetSection("LastFm:Secret").Value;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
            this._lastFMClient = new LastfmClient(this._key, this._secret);
        }


        public async Task<int> UpdateUser(User user)
        {
            Thread.Sleep(750);

            Log.Information($"Updating {user.UserNameLastFM}");

            var recentTracks = await this._lastFMClient.User.GetRecentScrobbles(user.UserNameLastFM, count: 1000);
            Statistics.LastfmApiCalls.Inc();

            if (!recentTracks.Success || !recentTracks.Content.Any())
            {
                Log.Warning($"Something went wrong getting recent tracks for {user.UserNameLastFM} | {recentTracks.Status}");
                return 0;
            }

            var newScrobbles = recentTracks.Content
                .Where(w => w.TimePlayed.HasValue && w.TimePlayed.Value.DateTime > user.LastScrobbleUpdate)
                .ToList();

            if (!newScrobbles.Any())
            {
                Log.Information($"No new scrobbles for {user.UserNameLastFM}");
                await SetUserUpdateTime(user.UserId, DateTime.UtcNow);
                return 0;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();

            await UpdateArtistsForUser(user, newScrobbles, cachedArtistAliases);

            await UpdateAlbumsForUser(user, newScrobbles, cachedArtistAliases);

            await UpdateTracksForUser(user, newScrobbles, cachedArtistAliases);

            var latestScrobbleDate = GetLatestScrobbleDate(newScrobbles);
            await SetUserUpdateAndScrobbleTime(user.UserId, DateTime.UtcNow, latestScrobbleDate);

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

        private async Task UpdateArtistsForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases)
        {
            foreach (var artist in newScrobbles.GroupBy(g => g.ArtistName))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == artist.Key.ToLower());

                var artistName = alias != null ? alias.Artist.Name : artist.Key;

                await using var connection = new NpgsqlConnection(this._connectionString);
                connection.Open();

                await using var updateArtistPlaycount =
                    new NpgsqlCommand(
                        $"UPDATE public.user_artists SET playcount = playcount + @playcountToAdd WHERE user_id = @userId AND UPPER(name) = UPPER(@name);",
                        connection);

                updateArtistPlaycount.Parameters.AddWithValue("playcountToAdd", artist.Count());
                updateArtistPlaycount.Parameters.AddWithValue("userId", user.UserId);
                updateArtistPlaycount.Parameters.AddWithValue("name", artistName);

                await updateArtistPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            Log.Information($"Updated artists for {user.UserNameLastFM}");
        }

        private async Task UpdateAlbumsForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases)
        {
            foreach (var album in newScrobbles.GroupBy(x => new { x.ArtistName, x.AlbumName }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == album.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : album.Key.ArtistName;

                await using var connection = new NpgsqlConnection(this._connectionString);
                connection.Open();

                await using var setIndexTime =
                    new NpgsqlCommand(
                        $"UPDATE public.user_albums SET playcount = playcount + @playcountToAdd WHERE user_id = @userId AND UPPER(name) = UPPER(@name) AND UPPER(artist_name) = UPPER(@artistName) ;",
                        connection);

                setIndexTime.Parameters.AddWithValue("playcountToAdd", album.Count());
                setIndexTime.Parameters.AddWithValue("userId", user.UserId);
                setIndexTime.Parameters.AddWithValue("name", album.Key.AlbumName);
                setIndexTime.Parameters.AddWithValue("artistName", artistName);

                await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            Log.Information($"Updated albums for {user.UserNameLastFM}");
        }

        private async Task UpdateTracksForUser(User user, IEnumerable<LastTrack> newScrobbles,
            IReadOnlyList<ArtistAlias> cachedArtistAliases)
        {
            foreach (var track in newScrobbles.GroupBy(x => new { x.ArtistName, x.Name }))
            {
                var alias = cachedArtistAliases
                    .FirstOrDefault(f => f.Alias.ToLower() == track.Key.ArtistName.ToLower());

                var artistName = alias != null ? alias.Artist.Name : track.Key.ArtistName;

                await using var connection = new NpgsqlConnection(this._connectionString);
                connection.Open();

                await using var setIndexTime =
                    new NpgsqlCommand(
                        $"UPDATE public.user_tracks SET playcount = playcount + @playcountToAdd WHERE user_id = @userId AND UPPER(name) = UPPER(@name) AND UPPER(artist_name) = UPPER(@artistName);",
                        connection);

                setIndexTime.Parameters.AddWithValue("playcountToAdd", track.Count());
                setIndexTime.Parameters.AddWithValue("userId", user.UserId);
                setIndexTime.Parameters.AddWithValue("name", track.Key.Name);
                setIndexTime.Parameters.AddWithValue("artistName", artistName);

                await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            Log.Information($"Updated tracks for {user.UserNameLastFM}");
        }

        private async Task SetUserUpdateAndScrobbleTime(int userId, DateTime now, DateTime lastScrobble)
        {
            await using var connection = new NpgsqlConnection(this._connectionString);
            connection.Open();

            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task SetUserUpdateTime(int userId, DateTime now)
        {
            await using var connection = new NpgsqlConnection(this._connectionString);
            connection.Open();

            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_updated = '{now:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
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
    }
}
