using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PostgreSQLCopyHelper;

namespace FMBot.LastFM.Services
{
    public class UpdateService
    {
        private readonly LastfmClient _lastFMClient;

        private readonly string _key;
        private readonly string _secret;

        private readonly string _connectionString;

        public UpdateService(IConfigurationRoot configuration)
        {
            this._key = configuration.GetSection("LastFm:Key").Value;
            this._secret = configuration.GetSection("LastFm:Secret").Value;
            this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
            this._lastFMClient = new LastfmClient(this._key, this._secret);
        }

        public async Task InitialUserIndex(User user)
        {
            Thread.Sleep(1000);

            Console.WriteLine($"Starting artist store for {user.UserNameLastFM}");

            var artists = await GetArtistForUserFromLastFm(user);

            await InsertArtistsIntoDatabase(artists, user.UserId, DateTime.UtcNow);
        }

        private async Task<IReadOnlyList<UserArtist>> GetArtistForUserFromLastFm(User user)
        {
            var topArtists = new List<LastArtist>();

            const int amountOfApiCalls = 4000 / 1000;
            for (var i = 1; i < amountOfApiCalls + 1; i++)
            {
                var artistResult = await this._lastFMClient.User.GetTopArtists(user.UserNameLastFM,
                    LastStatsTimeSpan.Overall, i, 1000);

                topArtists.AddRange(artistResult);

                if (artistResult.Count() < 1000)
                {
                    break;
                }
            }

            if (topArtists.Count == 0)
            {
                return new List<UserArtist>();
            }

            return topArtists.Select(a => new UserArtist
            {
                LastUpdated = DateTime.UtcNow,
                Name = a.Name,
                Playcount = a.PlayCount.Value,
                UserId = user.UserId
            }).ToList();
        }

        private async Task InsertArtistsIntoDatabase(IReadOnlyList<UserArtist> artists, int userId, DateTime now)
        {
            var copyHelper = new PostgreSQLCopyHelper<UserArtist>("public", "user_artists")
                .MapText("name", x => x.Name)
                .MapInteger("user_id", x => x.UserId)
                .MapInteger("playcount", x => x.Playcount)
                .MapTimeStamp("last_updated", x => x.LastUpdated);

            await using var connection = new NpgsqlConnection(this._connectionString);
            connection.Open();

            await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.user_artists WHERE user_id = {userId};", connection);
            await deleteCurrentArtists.ExecuteNonQueryAsync().ConfigureAwait(false);

            await copyHelper.SaveAllAsync(connection, artists).ConfigureAwait(false);

            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
