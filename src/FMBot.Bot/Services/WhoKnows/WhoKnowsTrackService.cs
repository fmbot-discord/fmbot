using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsTrackService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly IdResolutionService _idResolutionService;

    public WhoKnowsTrackService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings,
        IMemoryCache cache, IdResolutionService idResolutionService)
    {
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
        this._cache = cache;
        this._idResolutionService = idResolutionService;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, string artistName, string trackName)
    {
        var trackId = await this._idResolutionService.ResolveTrackId(artistName, trackName);
        if (!trackId.HasValue)
        {
            return new List<WhoKnowsObjectWithUser>();
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var whoKnowsTrackList =
            await WhoKnowsRepository.GetIndexedUsersForTrack(guildUsers, guildId, trackId.Value, connection);

        return whoKnowsTrackList.WithDiscordDisplayNames(discordGuild, guildUsers);
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForTrack(NetCord.Gateway.Guild discordGuild,
        string artistName, string trackName)
    {
        var trackId = await this._idResolutionService.ResolveTrackId(artistName, trackName);
        if (!trackId.HasValue)
        {
            return new List<WhoKnowsObjectWithUser>();
        }

        const string sql = "SELECT * " +
                           "FROM(SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ut.user_id, " +
                           "ut.playcount," +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.registered_last_fm, " +
                           "u.privacy_level, " +
                           "u.last_used " +
                           "FROM user_tracks AS ut " +
                           "FULL OUTER JOIN users AS u ON ut.user_id = u.user_id " +
                           "WHERE ut.track_id = @trackId " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ut.playcount DESC) ut " +
                           "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userTracks = (await connection.QueryAsync<WhoKnowsGlobalTrackDto>(sql, new
        {
            trackId = trackId.Value
        })).ToList();

        var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

        for (var i = 0; i < userTracks.Count; i++)
        {
            var userTrack = userTracks[i];

            var userName = userTrack.UserNameLastFm;

            if (i < 15)
            {
                if (discordGuild != null && discordGuild.Users.TryGetValue(userTrack.DiscordUserId, out var discordUser))
                {
                    userName = discordUser.GetDisplayName();
                }
            }

            whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userTrack.Playcount,
                LastFMUsername = userTrack.UserNameLastFm,
                UserId = userTrack.UserId,
                RegisteredLastFm = userTrack.RegisteredLastFm,
                PrivacyLevel = userTrack.PrivacyLevel,
            });
        }

        return whoKnowsTrackList;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForTrack(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int userId, string artistName, string trackName)
    {
        var trackId = await this._idResolutionService.ResolveTrackId(artistName, trackName);
        if (!trackId.HasValue)
        {
            return new List<WhoKnowsObjectWithUser>();
        }

        const string sql = "SELECT ut.user_id, " +
                           "ut.playcount, " +
                           "u.user_name_last_fm " +
                           "FROM user_tracks AS ut " +
                           "FULL OUTER JOIN users AS u ON ut.user_id = u.user_id " +
                           "INNER JOIN friends AS fr ON fr.friend_user_id = ut.user_id " +
                           "LEFT JOIN guild_users AS gu ON gu.user_id = ut.user_id AND gu.guild_id = @guildId " +
                           "WHERE fr.user_id = @userId AND ut.track_id = @trackId " +
                           "ORDER BY ut.playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userArtists = (await connection.QueryAsync<WhoKnowsTrackDto>(sql, new
        {
            trackId = trackId.Value,
            guildId,
            userId
        })).ToList();

        var whoKnowsArtistList = new List<WhoKnowsObjectWithUser>();

        foreach (var userArtist in userArtists)
        {
            var userName = userArtist.UserNameLastFm;

            guildUsers.TryGetValue(userArtist.UserId, out var guildUser);
            if (discordGuild != null && guildUser != null)
            {
                userName = guildUser.UserName;

                if (discordGuild.Users.TryGetValue(guildUser.DiscordUserId, out var discordGuildUser))
                {
                    userName = discordGuildUser.GetDisplayName();
                }
            }

            whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userArtist.Playcount,
                LastFMUsername = userArtist.UserNameLastFm,
                UserId = userArtist.UserId
            });
        }

        return whoKnowsArtistList;
    }

    public async Task<int?> GetTrackPlayCountForUser(string artistName, string trackName, int userId)
    {
        var trackId = await this._idResolutionService.ResolveTrackId(artistName, trackName);
        if (!trackId.HasValue)
        {
            return null;
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await TrackRepository.GetTrackPlayCountForUser(connection, trackId.Value, userId);
    }

    public async Task<ICollection<GuildTrack>> GetTopAllTimeTracksForGuild(int guildId,
        OrderType orderType, string artistName, int[] userIds = null)
    {
        var cacheKey = $"guild-alltime-top-tracks-{guildId}-{orderType}-{artistName}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out ICollection<GuildTrack> cachedTracks))
        {
            return cachedTracks;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var results =
            await WhoKnowsRepository.GetTopAllTimeTracksForGuild(guildId, orderType, artistName, userIds, connection);

        if (userIds == null)
        {
            this._cache.Set(cacheKey, results, TimeSpan.FromMinutes(10));
        }

        return results;
    }
}
