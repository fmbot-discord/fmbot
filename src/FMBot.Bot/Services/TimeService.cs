using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services
{
    public class TimeService
    {
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;

        public TimeService(IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public async Task<TimeSpan> GetPlayTimeForPlays(IEnumerable<UserPlay> plays)
        {
            long totalMs = 0;
            await CacheAllTrackLengths();

            foreach (var userPlay in plays)
            {
                var length = (long?)this._cache.Get(CacheKeyForTrack(userPlay.TrackName.ToLower(), userPlay.ArtistName.ToLower()));

                if (length.HasValue)
                {
                    totalMs += length.Value;
                }
                else
                {
                    var artistLength = (long?)this._cache.Get(CacheKeyForArtist(userPlay.ArtistName.ToLower()));


                    if (artistLength.HasValue)
                    {
                        totalMs += artistLength.Value;
                    }
                    else
                    {
                        // Average song length
                        totalMs += 210000;
                    }
                }
            }

            return TimeSpan.FromMilliseconds(totalMs);
        }

        private async Task CacheAllTrackLengths()
        {
            const string cacheKey = "track-lengths-cached";
            var cacheTime = TimeSpan.FromMinutes(10);

            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            const string sql = "SELECT LOWER(artist_name) as artist_name, LOWER(name) as track_name, duration_ms " +
                               "FROM public.tracks where duration_ms is not null;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var trackLengths = (await connection.QueryAsync<TrackLengthDto>(sql)).ToList();

            foreach (var length in trackLengths)
            {
                this._cache.Set(CacheKeyForTrack(length.TrackName, length.ArtistName), length.DurationMs, cacheTime);
            }

            foreach (var artistLength in trackLengths.GroupBy(g => g.ArtistName))
            {
                this._cache.Set(CacheKeyForArtist(artistLength.Key), (long)artistLength.Average(a => a.DurationMs), cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        private static string CacheKeyForTrack(string trackName, string artistName)
        {
            return $"track-length-{trackName}-{artistName}";
        }
        private static string CacheKeyForArtist(string artistName)
        {
            return $"artist-length-avg-{artistName}";
        }
    }
}
