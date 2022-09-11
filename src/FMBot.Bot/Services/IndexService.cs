using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
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

namespace FMBot.Bot.Services
{
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
            this._userIndexQueue.Publish(users);
        }

        public async Task IndexUser(User user)
        {
            Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

            if (!this._cache.TryGetValue($"index-started-{user.UserId}", out bool _))
            {
                await this._indexRepository.IndexUser(new IndexUserQueueItem(user.UserId));
            }
            else
            {
                Log.Information("Index for {UserNameLastFM} already in progress, skipping.", user.UserNameLastFM);
            }
        }

        public async Task<(int, int?)> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers)
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
                    TitlesEnabled = true,
                    Name = discordGuild.Name,
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

            int? whoKnowsWhitelistedCount = null;
            if (existingGuild.WhoKnowsWhitelistRoleId.HasValue)
            {
                whoKnowsWhitelistedCount = 0;
            }

            foreach (var user in users)
            {
                var discordUser = discordGuildUsers.First(f => f.Id == user.User.DiscordUserId);
                var name = discordUser.Nickname ?? discordUser.Username;
                user.UserName = name;
                user.Bot = discordUser.IsBot;

                if (existingGuild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    var isWhitelisted = discordUser.RoleIds.Contains(existingGuild.WhoKnowsWhitelistRoleId.Value);
                    user.WhoKnowsWhitelisted = isWhitelisted;
                    if (isWhitelisted)
                    {
                        whoKnowsWhitelistedCount++;
                    }
                }
            }

            var connString = db.Database.GetDbConnection().ConnectionString;
            var copyHelper = new PostgreSQLCopyHelper<GuildUser>("public", "guild_users")
                .MapInteger("guild_id", x => x.GuildId)
                .MapInteger("user_id", x => x.UserId)
                .MapText("user_name", x => x.UserName)
                .MapBoolean("bot", x => x.Bot == true)
                .MapBoolean("who_knows_whitelisted", x => x.WhoKnowsWhitelisted);

            await using var connection = new NpgsqlConnection(connString);
            await connection.OpenAsync();

            await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.guild_users WHERE guild_id = {existingGuild.GuildId};", connection);
            await deleteCurrentArtists.ExecuteNonQueryAsync();

            await copyHelper.SaveAllAsync(connection, users);

            Log.Information("Stored guild users for guild with id {guildId}", existingGuild.GuildId);

            await connection.CloseAsync();

            return (users.Count, whoKnowsWhitelistedCount);
        }

        public async Task<GuildUser> GetOrAddUserToGuild(ICollection<WhoKnowsObjectWithUser> users,
            Persistence.Domain.Models.Guild guild,
            IGuildUser discordGuildUser,
            User user)
        {
            try
            {
                var foundUser = users.FirstOrDefault(f => f.UserId == user.UserId);

                if (foundUser == null)
                {
                    await using var db = await this._contextFactory.CreateDbContextAsync();
                    var existingGuildUser = await db.GuildUsers
                        .AsQueryable()
                        .FirstOrDefaultAsync(a => a.GuildId == guild.GuildId && a.UserId == user.UserId);

                    if (existingGuildUser != null)
                    {
                        return existingGuildUser;
                    }

                    var guildUserToAdd = new GuildUser
                    {
                        GuildId = guild.GuildId,
                        UserId = user.UserId,
                        UserName = discordGuildUser.Nickname ?? discordGuildUser.Username
                    };

                    if (guild.WhoKnowsWhitelistRoleId.HasValue)
                    {
                        guildUserToAdd.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                    }

                    await AddGuildUserToDatabase(guildUserToAdd);

                    guildUserToAdd.User = user;

                    return guildUserToAdd;
                }

                return new GuildUser
                {
                    Bot = false,
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser?.DisplayName ?? foundUser.DiscordName,
                    WhoKnowsWhitelisted = foundUser.WhoKnowsWhitelisted,
                    User = user
                };
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while attempting to add user {userId} to guild {guildId}", user.UserId, guild.GuildId);
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
            const string sql = "INSERT INTO guild_users (guild_id, user_id, user_name, bot, who_knows_whitelisted) " +
                               "VALUES (@guildId, @userId, @userName, false, @whoKnowsWhitelisted) " +
                               "ON CONFLICT DO NOTHING";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(sql, new
            {
                guildId = guildUserToAdd.GuildId,
                userId = guildUserToAdd.UserId,
                userName = guildUserToAdd.UserName,
                whoKnowsWhitelisted = guildUserToAdd.WhoKnowsWhitelisted
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

                var discordName = discordGuildUser.Nickname ?? discordGuildUser.Username;

                await using var db = await this._contextFactory.CreateDbContextAsync();

                const string sql = "UPDATE guild_users " +
                                   "SET user_name =  @UserName, " +
                                   "who_knows_whitelisted =  @WhoKnowsWhitelisted " +
                                   "WHERE guild_id = @GuildId AND user_id = @UserId ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var dto = new IndexedUserUpdateDto
                {
                    UserName = discordName,
                    GuildId = guild.GuildId,
                    UserId = userId
                };

                if (guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    dto.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                }

                await connection.ExecuteAsync(sql, dto);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception in UpdateUser!");
            }
        }

        public async Task UpdateGuildUserEvent(IGuildUser discordGuildUser)
        {
            try
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();
                var user = await db.Users
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.DiscordUserId == discordGuildUser.Id);

                if (user == null)
                {
                    return;
                }

                var guild = await db.Guilds
                    .Include(i => i.GuildUsers)
                    .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildUser.GuildId);

                if (guild?.GuildUsers == null || !guild.GuildUsers.Any())
                {
                    return;
                }

                var existingGuildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == user.UserId);

                if (existingGuildUser == null)
                {
                    var newGuildUser = new GuildUser
                    {
                        Bot = false,
                        GuildId = guild.GuildId,
                        UserId = user.UserId,
                        UserName = discordGuildUser?.DisplayName,
                    };

                    if (guild.WhoKnowsWhitelistRoleId.HasValue && discordGuildUser != null)
                    {
                        newGuildUser.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                    }

                    await AddGuildUserToDatabase(newGuildUser);
                    return;
                }

                const string sql = "UPDATE guild_users " +
                                   "SET user_name =  @UserName, " +
                                   "who_knows_whitelisted =  @whoKnowsWhitelisted " +
                                   "WHERE guild_id = @guildId AND user_id = @userId ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var dto = new IndexedUserUpdateDto
                {
                    UserName = discordGuildUser.DisplayName,
                    GuildId = guild.GuildId,
                    UserId = user.UserId
                };

                if (guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    dto.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                }

                await connection.ExecuteAsync(sql, dto);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception in UpdateDiscordUser!");
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
            return await db.Users
                .AsQueryable()
                .Where(f => f.LastIndexed != null &&
                            f.LastUpdated != null &&
                            f.LastIndexed <= timeLastIndexed)
                .OrderBy(o => o.LastUpdated)
                .ToListAsync();
        }
    }
}
