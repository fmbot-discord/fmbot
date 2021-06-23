using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsPlayService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;

        public WhoKnowsPlayService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public async Task<IReadOnlyList<ListArtist>> GetTopArtistsForGuild(int guildId, OrderType orderType, int amountOfDays)
        {
            var sql = "SELECT up.artist_name, " +
                      "COUNT(up.user_play_id) AS total_playcount, " +
                      "COUNT(DISTINCT up.user_id) AS listener_count " +
                      "FROM user_plays AS up " +
                      "INNER JOIN users AS u ON up.user_id = u.user_id  " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                      $"WHERE gu.guild_id = @guildId AND gu.bot != true AND time_played > current_date - interval '{amountOfDays}' day " +
                      "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                      "GROUP BY up.artist_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<ListArtist>(sql, new
            {
                guildId
            })).ToList();
        }

        public async Task<IReadOnlyList<ListAlbum>> GetTopAlbumsForGuild(int guildId, OrderType orderType, int amountOfDays)
        {
            var sql = "SELECT up.artist_name, up.album_name, " +
                      "COUNT(up.user_play_id) AS total_playcount, " +
                      "COUNT(DISTINCT up.user_id) AS listener_count " +
                      "FROM user_plays AS up " +
                      "INNER JOIN users AS u ON up.user_id = u.user_id  " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                      $"WHERE gu.guild_id = @guildId AND gu.bot != true AND time_played > current_date - interval '{amountOfDays}' day " +
                      "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                      "GROUP BY up.artist_name, album_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<ListAlbum>(sql, new
            {
                guildId
            })).ToList();
        }

        public async Task<IReadOnlyList<ListTrack>> GetTopTracksForGuild(int guildId, OrderType orderType, int amountOfDays)
        {
            var sql = "SELECT up.artist_name, up.track_name, " +
                      "COUNT(up.user_play_id) AS total_playcount, " +
                      "COUNT(DISTINCT up.user_id) AS listener_count " +
                      "FROM user_plays AS up " +
                      "INNER JOIN users AS u ON up.user_id = u.user_id  " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                      $"WHERE gu.guild_id = @guildId  AND gu.bot != true AND time_played > current_date - interval '{amountOfDays}' day " +
                      "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                      "GROUP BY up.artist_name, track_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<ListTrack>(sql, new
            {
                guildId
            })).ToList();
        }

        public void AddRecentPlayToCache(int userId, RecentTrack track)
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

        public async Task<string> GuildAlsoPlayingTrack(int userId, ulong discordGuildId, string artistName, string trackName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId != userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild == null || !guild.GuildUsers.Any())
            {
                return null;
            }

            var foundUsers = new List<GuildUser>();
            var userPlays = new List<UserPlay>();

            foreach (var user in guild.GuildUsers)
            {
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.TrackName == trackName.ToLower())
                {
                    foundUsers.Add(user);
                    userPlays.Add(userPlay);
                }
            }

            if (!foundUsers.Any())
            {
                return null;
            }

            if (foundUsers.Count == 1)
            {
                return $"{foundUsers.First().UserName} was also listening to this track {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this track!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this track!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this track!";
            }

            return null;
        }

        public async Task<string> GuildAlsoPlayingAlbum(int userId, ulong discordGuildId, string artistName, string albumName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId != userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild == null || !guild.GuildUsers.Any())
            {
                return null;
            }

            var foundUsers = new List<GuildUser>();
            var userPlays = new List<UserPlay>();

            foreach (var user in guild.GuildUsers)
            {
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.AlbumName == albumName.ToLower())
                {
                    foundUsers.Add(user);
                    userPlays.Add(userPlay);
                }
            }

            if (!foundUsers.Any())
            {
                return null;
            }

            if (foundUsers.Count == 1)
            {
                return $"{foundUsers.First().UserName} was also listening to this album {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this album!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this album!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this album!";
            }

            return null;
        }

        public async Task<string> GuildAlsoPlayingArtist(int userId, ulong discordGuildId, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId != userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild == null || !guild.GuildUsers.Any())
            {
                return null;
            }

            var foundUsers = new List<GuildUser>();
            var userPlays = new List<UserPlay>();

            foreach (var user in guild.GuildUsers)
            {
                var userFound = this._cache.TryGetValue($"{user.UserId}-last-play", out UserPlay userPlay);

                if (userFound && userPlay.ArtistName == artistName.ToLower() && userPlay.TimePlayed > DateTime.UtcNow.AddMinutes(-10))
                {
                    foundUsers.Add(user);
                    userPlays.Add(userPlay);
                }
            }

            if (!foundUsers.Any())
            {
                return null;
            }

            if (foundUsers.Count == 1)
            {
                return $"{foundUsers.First().UserName} was also listening to this artist {StringExtensions.GetTimeAgo(userPlays.First().TimePlayed)}!";
            }
            if (foundUsers.Count == 2)
            {
                return $"{foundUsers[0].UserName} and {foundUsers[1].UserName} were also recently listening to this artist!";
            }
            if (foundUsers.Count == 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName} and {foundUsers[2].UserName} were also recently listening to this artist!";
            }
            if (foundUsers.Count > 3)
            {
                return $"{foundUsers[0].UserName}, {foundUsers[1].UserName}, {foundUsers[2].UserName} and {foundUsers.Count - 3} others were also recently listening to this artist!";
            }

            return null;
        }

        private class ArtistUserPlay
        {
            public string ArtistName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }

        private class AlbumUserPlay
        {
            public string ArtistName { get; set; }

            public string AlbumName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }

        private class TrackUserPlay
        {
            public string ArtistName { get; set; }

            public string TrackName { get; set; }

            public int UserId { get; set; }

            public int Playcount { get; set; }
        }
    }
}
