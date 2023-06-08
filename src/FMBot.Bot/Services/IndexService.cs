using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Bot.Services;

public class IndexService : IIndexService
{
    private readonly IUserIndexQueue _userIndexQueue;
    private readonly IndexRepository _indexRepository;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly LastFmRepository _lastFmRepository;

    public IndexService(IUserIndexQueue userIndexQueue,
        IndexRepository indexRepository,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings, LastFmRepository lastFmRepository)
    {
        this._userIndexQueue = userIndexQueue;
        this._userIndexQueue.UsersToIndex.SubscribeAsync(OnNextAsync);
        this._indexRepository = indexRepository;
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._lastFmRepository = lastFmRepository;
        this._botSettings = botSettings.Value;
    }

    private async Task OnNextAsync(IndexUserQueueItem user)
    {
        await this._indexRepository.IndexUser(user);
    }

    public void AddUsersToIndexQueue(IReadOnlyList<User> users)
    {
        Log.Information($"Adding {users.Count} users to index queue");

        this._userIndexQueue.Publish(users);
    }

    public async Task<IndexedUserStats> IndexUser(User user)
    {
        Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

        if (!this._cache.TryGetValue($"index-started-{user.UserId}", out bool _))
        {
            return await this._indexRepository.IndexUser(new IndexUserQueueItem(user.UserId));
        }
        else
        {
            Log.Information("Index for {UserNameLastFM} already in progress, skipping.", user.UserNameLastFM);
        }

        return null;
    }

    public async Task<int> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();

            existingGuild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);
        }

        var users = await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId))
            .Select(s => new GuildUser
            {
                GuildId = existingGuild.GuildId,
                UserId = s.UserId,
                User = s,
            })
            .ToListAsync();

        foreach (var user in users)
        {
            var discordUser = discordGuildUsers.First(f => f.Id == user.User.DiscordUserId);

            user.UserName = discordUser.DisplayName;
            user.Bot = discordUser.IsBot;

            if (PublicProperties.PremiumServers.ContainsKey(discordGuild.Id))
            {
                user.Roles = discordUser.RoleIds.ToArray();
            }

            if (existingGuild.GuildUsers != null && existingGuild.GuildUsers.Any())
            {
                var existingGuildUser = existingGuild.GuildUsers.FirstOrDefault(f => f.UserId == user.UserId);
                if (existingGuildUser != null)
                {
                    user.LastMessage = existingGuildUser.LastMessage;
                }
            }
        }

        var connString = db.Database.GetDbConnection().ConnectionString;
        var copyHelper = new PostgreSQLCopyHelper<GuildUser>("public", "guild_users")
            .MapInteger("guild_id", x => x.GuildId)
            .MapInteger("user_id", x => x.UserId)
            .MapText("user_name", x => x.UserName)
            .MapBoolean("bot", x => x.Bot == true)
            .MapArray("roles", x => x.Roles?.Select(s => (decimal)s).ToArray())
            .MapTimeStampTz("last_message", x => x.LastMessage.HasValue ? DateTime.SpecifyKind(x.LastMessage.Value, DateTimeKind.Utc) : null);

        await using var connection = new NpgsqlConnection(connString);
        await connection.OpenAsync();

        await using var deleteCurrentUsers = new NpgsqlCommand($"DELETE FROM public.guild_users WHERE guild_id = {existingGuild.GuildId};", connection);
        await deleteCurrentUsers.ExecuteNonQueryAsync();

        await copyHelper.SaveAllAsync(connection, users);

        Log.Information("GuildUserUpdate: Stored guild users for guild with id {guildId}", existingGuild.GuildId);

        await connection.CloseAsync();

        return users.Count();
    }

    public async Task<GuildUser> GetOrAddUserToGuild(
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        IGuildUser discordGuildUser,
        User user)
    {
        try
        {
            if (!guildUsers.TryGetValue(user.UserId, out var guildUser))
            {
                var guildUserToAdd = new GuildUser
                {
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser.DisplayName
                };

                if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
                {
                    guildUserToAdd.Roles = discordGuildUser.RoleIds.ToArray();
                }

                await AddGuildUserToDatabase(guildUserToAdd);

                guildUserToAdd.User = user;

                return guildUserToAdd;
            }
            else
            {
                guildUser.UserName = discordGuildUser.DisplayName;

                return new GuildUser
                {
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser.DisplayName,
                    Roles = discordGuildUser.RoleIds.ToArray(),
                    Bot = false,
                    LastMessage = DateTime.UtcNow,
                    User = user
                };
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Error while attempting to add user {userId} to guild {guildId}", user.UserId, guild.GuildId);
            return new GuildUser
            {
                GuildId = guild.GuildId,
                UserId = user.UserId,
                User = user,
                UserName = discordGuildUser?.DisplayName ?? user.UserNameLastFM
            };
        }
    }

    public async Task AddGuildUserToDatabase(GuildUser guildUserToAdd)
    {
        const string sql = "INSERT INTO guild_users (guild_id, user_id, user_name, bot, roles, last_message) " +
                           "VALUES (@guildId, @userId, @userName, false, @roles, @lastMessage) " +
                           "ON CONFLICT DO NOTHING";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(sql, new
        {
            guildId = guildUserToAdd.GuildId,
            userId = guildUserToAdd.UserId,
            userName = guildUserToAdd.UserName,
            roles = guildUserToAdd.Roles?.Select(s => (decimal)s).ToArray(),
            lastMessage = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        });

        Log.Information("Added user {guildUserName} | {userId} to guild {guildName}", guildUserToAdd.UserName, guildUserToAdd.UserId, guildUserToAdd.GuildId);
    }

    public async Task UpdateGuildUser(IGuildUser discordGuildUser, int userId, Persistence.Domain.Models.Guild guild)
    {
        try
        {
            if (discordGuildUser == null)
            {
                return;
            }

            const string sql = "UPDATE guild_users " +
                               "SET user_name =  @UserName, " +
                               "roles =  @Roles " +
                               "WHERE guild_id = @GuildId AND user_id = @UserId ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var dto = new IndexedUserUpdateDto
            {
                UserName = discordGuildUser.DisplayName,
                GuildId = guild.GuildId,
                UserId = userId
            };

            if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
            {
                dto.Roles = discordGuildUser.RoleIds.Select(s => (decimal)s).ToArray();
            }

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Exception in UpdateUser!");
        }
    }

    public async Task AddOrUpdateGuildUser(IGuildUser discordGuildUser)
    {
        try
        {
            if (!PublicProperties.RegisteredUsers.TryGetValue(discordGuildUser.Id, out var userId))
            {
                return;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();

            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId == userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildUser.GuildId);

            if (guild?.GuildUsers == null || !guild.GuildUsers.Any())
            {
                return;
            }

            var existingGuildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == userId);

            if (existingGuildUser == null)
            {
                var newGuildUser = new GuildUser
                {
                    Bot = false,
                    GuildId = guild.GuildId,
                    UserId = userId,
                    UserName = discordGuildUser?.DisplayName,
                };

                if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
                {
                    newGuildUser.Roles = discordGuildUser.RoleIds.ToArray();
                }

                await AddGuildUserToDatabase(newGuildUser);
                return;
            }

            const string sql = "UPDATE guild_users " +
                               "SET user_name =  @UserName, " +
                               "roles =  @Roles " +
                               "WHERE guild_id = @guildId AND user_id = @userId ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var dto = new IndexedUserUpdateDto
            {
                UserName = discordGuildUser.DisplayName,
                GuildId = guild.GuildId,
                UserId = userId
            };

            if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
            {
                dto.Roles = discordGuildUser.RoleIds.Select(s => (decimal)s).ToArray();
            }

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Exception in UpdateDiscordUser!");
        }
    }

    public async Task RemoveUserFromGuild(ulong discordUserId, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var userThatLeft = await db.Users
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (userThatLeft == null)
        {
            return;
        }

        var guild = await db.Guilds
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (guild?.GuildUsers != null && guild.GuildUsers.Any() && guild.GuildUsers.Select(g => g.UserId).Contains(userThatLeft.UserId))
        {
            var guildUser = guild
                .GuildUsers
                .FirstOrDefault(f => f.UserId == userThatLeft.UserId && f.GuildId == guild.GuildId);

            if (guildUser != null)
            {
                db.GuildUsers.Remove(guildUser);

                await db.SaveChangesAsync();
            }
        }
    }

    public async Task<DateTime?> AddUserRegisteredLfmDate(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (user == null)
        {
            return null;
        }

        return await this._indexRepository.SetUserSignUpTime(user);
    }

    public async Task<IReadOnlyList<User>> GetUsersToFullyUpdate(IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId) &&
                        (w.LastIndexed == null || w.LastUpdated == null))
            .ToListAsync();
    }

    public async Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId)
                        && w.LastIndexed != null)
            .CountAsync();
    }

    public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var recentlyUsed = DateTime.UtcNow.AddDays(-2);
        return await db.Users
            .AsQueryable()
            .Where(f => f.LastIndexed != null &&
                        f.LastUpdated != null &&
                        f.LastUsed >= recentlyUsed &&
                        f.LastIndexed <= timeLastIndexed)
            .OrderBy(o => o.LastUsed)
            .ToListAsync();
    }
}
