using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;

namespace FMBot.Bot.Services.WhoKnows;

public class CrownService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly BotSettings _botSettings;
    private readonly IUpdateService _updateService;

    public CrownService(IDbContextFactory<FMBotDbContext> contextFactory, IDataSourceFactory dataSourceFactory, IOptions<BotSettings> botSettings, IUpdateService updateService)
    {
        this._contextFactory = contextFactory;
        this._dataSourceFactory = dataSourceFactory;
        this._updateService = updateService;
        this._botSettings = botSettings.Value;
    }

    public async Task<CrownModel> GetAndUpdateCrownForArtist(List<WhoKnowsObjectWithUser> users, Persistence.Domain.Models.Guild guild, string artistName)
    {
        var eligibleUsers = users.ToList();

        if (guild.CrownsActivityThresholdDays.HasValue)
        {
            eligibleUsers = eligibleUsers
                .Where(w => w.LastUsed != null &&
                            w.LastUsed >= DateTime.UtcNow.AddDays(-guild.CrownsActivityThresholdDays.Value))
                .ToList();
        }
        if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromCrowns))
        {
            var usersToFilter = guild.GuildBlockedUsers
                .Where(wh => wh.BlockedFromCrowns)
                .Select(s => s.UserId)
                .Distinct()
                .ToHashSet();

            eligibleUsers = eligibleUsers.Where(w =>
                    !usersToFilter.Contains(w.UserId))
                .ToList();
        }
        //if (guild.WhoKnowsWhitelistRoleId.HasValue)
        //{
        //    eligibleUsers = eligibleUsers.Where(w => w.WhoKnowsWhitelisted != false)
        //        .ToList();
        //}

        var eligibleUserIds = eligibleUsers
            .Select(s => s.UserId)
            .Distinct()
            .ToHashSet();

        var topUser = users
            .Where(w => eligibleUserIds.Contains(w.UserId) &&
                        (guild.CrownsMinimumPlaycountThreshold.HasValue ? w.Playcount >= guild.CrownsMinimumPlaycountThreshold : w.Playcount >= Constants.DefaultPlaysForCrown))
            .MaxBy(o => o.Playcount);

        if (topUser == null)
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var currentCrownHolder = await GetCurrentCrownHolder(connection, guild.GuildId, artistName);
        WhoKnowsObjectWithUser currentCrownHolderUser = null;
        if (currentCrownHolder != null)
        {
            currentCrownHolderUser = users.FirstOrDefault(f => f.UserId == currentCrownHolder.UserId);
        }

        // Crown exists and is same as top user
        if (currentCrownHolder != null && topUser.UserId == currentCrownHolder.UserId)
        {
            var oldPlaycount = currentCrownHolder.CurrentPlaycount;
            if (oldPlaycount < topUser.Playcount)
            {
                currentCrownHolder.CurrentPlaycount = topUser.Playcount;
                currentCrownHolder.Modified = DateTime.UtcNow;
                currentCrownHolder.SeededCrown = false;

                await UpdateCrown(connection, currentCrownHolder.CrownId, currentCrownHolder);

                return new CrownModel
                {
                    Crown = currentCrownHolder
                };
            }

            return new CrownModel
            {
                Crown = currentCrownHolder
            };
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        // Crown exists, but top user is a different person
        if (currentCrownHolder != null && topUser.UserId != currentCrownHolder.UserId)
        {
            var crownUser = await db.Users
                .AsNoTracking()
                .FirstAsync(f => f.UserId == currentCrownHolder.UserId);

            var currentPlaycountForCrownHolder =
                await GetCurrentPlaycountForUser(artistName, crownUser.UserNameLastFM, currentCrownHolder.UserId) ?? currentCrownHolder.CurrentPlaycount;

            if (PublicProperties.IssuesAtLastFm)
            {
                return new CrownModel
                {
                    Crown = currentCrownHolder,
                    CrownResult = "*Crown stealing is currently disabled due to issues with the Last.fm API*",
                    CrownHtmlResult = "Crown stealing is currently disabled due to issues with the Last.fm API"
                };
            }

            var currentCrownHolderIndex = users.IndexOf(currentCrownHolderUser);

            // Current crownholder playcount is still higher after extra check
            if (eligibleUserIds.Contains(currentCrownHolder.UserId) && currentPlaycountForCrownHolder >= topUser.Playcount)
            {
                currentCrownHolder.CurrentPlaycount = topUser.Playcount;
                currentCrownHolder.Modified = DateTime.UtcNow;

                if (currentCrownHolderIndex != -1 && users[currentCrownHolderIndex]?.UserId == currentCrownHolder.UserId)
                {
                    users[currentCrownHolderIndex].Playcount = (int)currentPlaycountForCrownHolder;
                }

                await UpdateCrown(connection, currentCrownHolder.CrownId, currentCrownHolder);

                return new CrownModel
                {
                    Crown = currentCrownHolder
                };
            }

            currentCrownHolder.Active = false;
            currentCrownHolder.Modified = DateTime.UtcNow;
            if (currentPlaycountForCrownHolder > currentCrownHolder.CurrentPlaycount)
            {
                currentCrownHolder.CurrentPlaycount = (int)currentPlaycountForCrownHolder;
            }

            if (currentCrownHolderIndex != -1 && users[currentCrownHolderIndex]?.UserId == currentCrownHolder.UserId)
            {
                users[currentCrownHolderIndex].Playcount = (int)currentPlaycountForCrownHolder;
            }

            await UpdateCrown(connection, currentCrownHolder.CrownId, currentCrownHolder);

            var newCrown = new UserCrown
            {
                UserId = topUser.UserId,
                GuildId = guild.GuildId,
                ArtistName = artistName,
                Active = true,
                Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                StartPlaycount = topUser.Playcount,
                CurrentPlaycount = topUser.Playcount,
                SeededCrown = false
            };

            await db.UserCrowns.AddAsync(newCrown);

            await db.SaveChangesAsync();

            await db.DisposeAsync();
            await connection.CloseAsync();

            var currentCrownHolderName = users.FirstOrDefault(f => f.UserId == currentCrownHolder.UserId)?.DiscordName;

            return new CrownModel
            {
                Crown = newCrown,
                CrownResult = $"Crown stolen by {topUser.DiscordName} with `{topUser.Playcount}` plays! \n" +
                              $"*Previous owner: {currentCrownHolderName ?? crownUser.UserNameLastFM} with `{currentCrownHolder.CurrentPlaycount}` plays*.",
                CrownHtmlResult = $"Crown stolen by <b>{topUser.DiscordName}</b> with <b>{topUser.Playcount} plays</b>! " +
                              $"Previous owner: <b>{currentCrownHolderName ?? crownUser.UserNameLastFM}</b> with <b>{currentCrownHolder.CurrentPlaycount} plays</b>.",
                Stolen = true
            };
        }

        var minAmountOfPlaysForCrown = guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown;

        // No crown exists yet
        if (currentCrownHolder == null)
        {
            if (topUser.Playcount >= minAmountOfPlaysForCrown)
            {
                var newCrown = new UserCrown
                {
                    UserId = topUser.UserId,
                    GuildId = guild.GuildId,
                    ArtistName = artistName,
                    Active = true,
                    Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    StartPlaycount = topUser.Playcount,
                    CurrentPlaycount = topUser.Playcount,
                    SeededCrown = false
                };

                await db.UserCrowns.AddAsync(newCrown);

                await db.SaveChangesAsync();

                await db.DisposeAsync();
                await connection.CloseAsync();

                return new Models.CrownModel
                {
                    Crown = newCrown,
                    CrownResult = $"Crown claimed by {topUser.DiscordName}!",
                    CrownHtmlResult = $"Crown claimed by <b>{topUser.DiscordName}</b>!"
                };
            }

            if (topUser.Playcount >= minAmountOfPlaysForCrown / 3)
            {
                var amountOfPlaysRequired = minAmountOfPlaysForCrown - topUser.Playcount;

                return new CrownModel
                {
                    CrownResult = $"{topUser.DiscordName} needs {amountOfPlaysRequired} more {StringExtensions.GetPlaysString(amountOfPlaysRequired)} to claim the crown.",
                    CrownHtmlResult = $"{topUser.DiscordName} needs {amountOfPlaysRequired} more {StringExtensions.GetPlaysString(amountOfPlaysRequired)} to claim the crown."
                };
            }
        }

        return null;
    }

    private static async Task<UserCrown> GetCurrentCrownHolder(NpgsqlConnection connection, int guildId, string artistName)
    {
        const string sql = "SELECT * FROM public.user_crowns AS uc " +
                           "WHERE uc.guild_id = @guildId AND " +
                           "uc.active = true AND " +
                           "UPPER(uc.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY current_playcount desc";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return await connection.QueryFirstOrDefaultAsync<UserCrown>(sql, new
        {
            guildId,
            artistName
        });
    }

    public static async Task<CurrentCrownHolderDto> GetCurrentCrownHolderWithName(NpgsqlConnection connection, int guildId, string artistName)
    {
        const string sql = "SELECT current_playcount, gu.user_id, gu.user_name " +
                           "FROM public.user_crowns AS uc  " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = uc.user_id AND gu.guild_id = @guildId " +
                           "WHERE uc.guild_id = @guildId AND uc.active = true AND  " +
                           "UPPER(uc.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY current_playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return await connection.QueryFirstOrDefaultAsync<CurrentCrownHolderDto>(sql, new
        {
            guildId,
            artistName
        });
    }

    private static async Task UpdateCrown(NpgsqlConnection connection, int crownId, UserCrown updatedCrown)
    {
        const string sql = "UPDATE public.user_crowns " +
                           "SET current_playcount = @currentPlaycount, " +
                           "modified = @modified, " +
                           "active = @active, " +
                           "seeded_crown = @seededCrown " +
                           "WHERE crown_id = @crownId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        await connection.QueryAsync(sql, new
        {
            crownId,
            currentPlaycount = updatedCrown.CurrentPlaycount,
            modified = updatedCrown.Modified,
            active = updatedCrown.Active,
            seededCrown = updatedCrown.SeededCrown
        });
    }

    private class UserIdArtistNameComparer : IEqualityComparer<(int UserId, string ArtistName)>
    {
        public bool Equals((int UserId, string ArtistName) x, (int UserId, string ArtistName) y)
        {
            return x.UserId == y.UserId && string.Equals(x.ArtistName, y.ArtistName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((int UserId, string ArtistName) obj)
        {
            return HashCode.Combine(obj.UserId, obj.ArtistName.ToLowerInvariant());
        }
    }

    public async Task<int> SeedCrownsForGuild(Persistence.Domain.Models.Guild guild, IList<UserCrown> existingCrowns)
    {
        const string sql = "SELECT DISTINCT ON(ua.name) " +
                           "ua.user_id, " +
                           "ua.name, " +
                           "ua.playcount " +
                           "FROM user_artists AS ua " +
                           "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                           "WHERE gu.guild_id = @guildId AND playcount >= @minPlaycount " +
                           "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_crowns = true AND guild_id = @guildId) " +
                           "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                           "ORDER BY ua.name, ua.playcount DESC;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var minPlaycount = guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown;

        var topUsersForPlaycount = await connection.QueryAsync<CrownSeedDto>(sql, new
        {
            guildId = guild.GuildId,
            minPlaycount
        });

        try
        {
            var existingActiveCrownsDict = existingCrowns?
                .OrderByDescending(o => o.CurrentPlaycount)
                .Where(w => w.Active)
                .DistinctBy(d => (d.UserId, d.ArtistName), new UserIdArtistNameComparer())
                .ToDictionary(c => (c.UserId, c.ArtistName.ToLower()));

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            var crownsToSeed = topUsersForPlaycount.Select(s =>
            {
                UserCrown existingCrown = null;
                existingActiveCrownsDict?.TryGetValue((s.UserId, s.Name.ToLower()), out existingCrown);
                return new UserCrown
                {
                    UserId = s.UserId,
                    Active = true,
                    ArtistName = s.Name,
                    Created = existingCrown?.Created ?? now,
                    Modified = now,
                    CurrentPlaycount = s.Playcount,
                    StartPlaycount = existingCrown?.StartPlaycount ?? s.Playcount,
                    GuildId = guild.GuildId,
                    SeededCrown = true
                };
            }).ToList();

            if (existingCrowns != null && existingCrowns.Any())
            {
                var existingCrownArtists = existingCrowns
                    .Where(wh => !wh.SeededCrown)
                    .Select(s => s.ArtistName.ToLower())
                    .ToHashSet();

                crownsToSeed = crownsToSeed.Where(w =>
                        !existingCrownArtists.Contains(w.ArtistName.ToLower()))
                    .ToList();
            }

            const string deleteOldSeededCrownsSql = "DELETE FROM public.user_crowns " +
                                                    "WHERE guild_id = @guildId AND seeded_crown = true;";

            await connection.ExecuteAsync(deleteOldSeededCrownsSql, new
            {
                guildId = guild.GuildId,
            });

            var copyHelper = new PostgreSQLCopyHelper<UserCrown>("public", "user_crowns")
                .MapInteger("guild_id", x => x.GuildId)
                .MapInteger("user_id", x => x.UserId)
                .MapText("artist_name", x => x.ArtistName)
                .MapInteger("current_playcount", x => x.CurrentPlaycount)
                .MapInteger("start_playcount", x => x.StartPlaycount)
                .MapTimeStampTz("created", x => DateTime.SpecifyKind(x.Created, DateTimeKind.Utc))
                .MapTimeStampTz("modified", x => DateTime.SpecifyKind(x.Created, DateTimeKind.Utc))
                .MapBoolean("active", x => x.Active)
                .MapBoolean("seeded_crown", x => x.SeededCrown);

            await copyHelper.SaveAllAsync(connection, crownsToSeed);
            await connection.CloseAsync();

            return crownsToSeed.Count;

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private async Task<long?> GetCurrentPlaycountForUser(string artistName, string lastFmUserName, int userId)
    {
        var artist = await this._dataSourceFactory.GetArtistInfoAsync(artistName, lastFmUserName);

        await this._updateService.UpdateUser(new UpdateUserQueueItem(userId));

        if (!artist.Success || !artist.Content.UserPlaycount.HasValue)
        {
            return null;
        }

        await this._updateService.CorrectUserArtistPlaycount(userId, artistName,
            artist.Content.UserPlaycount.Value);

        return artist.Content.UserPlaycount;
    }

    public async Task<IList<UserCrown>> GetCrownsForArtist(int guildId, string artistName)
    {
        const string sql = "SELECT * FROM public.user_crowns AS uc " +
                           "WHERE uc.guild_id = @guildId AND " +
                           "UPPER(uc.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY current_playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<UserCrown>(sql, new
        {
            guildId,
            artistName
        })).ToList();
    }

    public async Task RemoveCrowns(IList<UserCrown> crowns)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        db.UserCrowns.RemoveRange(crowns);

        await db.SaveChangesAsync();
    }

    public async Task<List<UserCrown>> GetCrownsForUser(Persistence.Domain.Models.Guild guild, int userId,
        CrownViewType crownViewType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        IQueryable<UserCrown> query;
        if (crownViewType != CrownViewType.Stolen)
        {
            query = db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.Active &&
                            f.UserId == userId);

            if (crownViewType == CrownViewType.Playcount)
            {
                query = query.OrderByDescending(o => o.CurrentPlaycount);
            }
            else
            {
                query = query.OrderByDescending(o => o.Created);
            }
        }
        else
        {
            query = db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(f => f.GuildId == guild.GuildId &&
                            !f.Active &&
                            f.UserId == userId)
                .OrderByDescending(o => o.Modified);
        }

        return await query.ToListAsync();
    }

    public async Task<List<IGrouping<int, UserCrown>>> GetTopCrownUsersForGuild(int guildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guildCrowns = await db.UserCrowns
            .AsQueryable()
            .Include(i => i.User)
            .Where(f => f.GuildId == guildId &&
                        f.Active)
            .ToListAsync();

        return guildCrowns
            .GroupBy(g => g.UserId)
            .OrderByDescending(o => o.Count())
            .ToList();
    }

    public async Task<int> GetTotalCrownCountForGuild(int guildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserCrowns
            .AsQueryable()
            .Include(i => i.User)
            .Where(f => f.GuildId == guildId &&
                        f.Active)
            .CountAsync();
    }

    public async Task<IList<UserCrown>> GetAllCrownsForGuild(int guildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserCrowns
            .AsQueryable()
            .Where(w => w.GuildId == guildId)
            .ToListAsync();
    }

    public async Task RemoveAllCrownsFromGuild(int guildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guildCrowns = await db.UserCrowns
            .AsQueryable()
            .Where(w => w.GuildId == guildId)
            .ToListAsync();

        db.UserCrowns.RemoveRange(guildCrowns);

        await db.SaveChangesAsync();
    }

    public async Task RemoveAllSeededCrownsFromGuild(Persistence.Domain.Models.Guild guild)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guildCrowns = await db.UserCrowns
            .AsQueryable()
            .Where(w => w.GuildId == guild.GuildId && w.SeededCrown)
            .ToListAsync();

        db.UserCrowns.RemoveRange(guildCrowns);

        await db.SaveChangesAsync();
    }

    public async Task RemoveAllCrownsFromUser(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var userCrowns = await db.UserCrowns
            .AsQueryable()
            .Where(f => f.UserId == userId)
            .ToListAsync();

        db.UserCrowns.RemoveRange(userCrowns);

        await db.SaveChangesAsync();
    }

    public async Task RemoveAllCrownsFromDiscordUser(ulong discordUserId, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var userThatLeft = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (userThatLeft == null)
        {
            return;
        }

        var guild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (guild != null)
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string deleteCrownsFromUserSql = "DELETE FROM public.user_crowns " +
                                                   "WHERE guild_id = @guildId AND user_id = @userId;";

            await connection.QueryAsync(deleteCrownsFromUserSql, new
            {
                guild.GuildId,
                userThatLeft.UserId
            });
        }
    }

}
