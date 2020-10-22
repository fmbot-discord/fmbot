using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Bot.Services
{
    public class IndexService : IIndexService
    {
        private readonly IUserIndexQueue _userIndexQueue;
        private readonly GlobalIndexService _globalIndexService;

        public IndexService(IUserIndexQueue userIndexQueue, GlobalIndexService indexService)
        {
            this._userIndexQueue = userIndexQueue;
            this._userIndexQueue.UsersToIndex.SubscribeAsync(OnNextAsync);
            this._globalIndexService = indexService;
        }

        private async Task OnNextAsync(User user)
        {
            Log.Verbose("User next up for index is {UserNameLastFM}", user.UserNameLastFM);
            await this._globalIndexService.IndexUser(user);
        }

        public void AddUsersToIndexQueue(IReadOnlyList<User> users)
        {
            Log.Information($"Starting index for {users.Count} users");

            this._userIndexQueue.Publish(users.ToList());
        }

        public async Task IndexUser(User user)
        {
            Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

            await this._globalIndexService.IndexUser(user);
        }

        public async Task StoreGuildUsers(IGuild guild, IReadOnlyCollection<IGuildUser> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.Id).ToList();

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var existingGuild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstAsync(f => f.DiscordGuildId == guild.Id);

            var users = await db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId))
                .Select(s => new GuildUser
                {
                    GuildId = existingGuild.GuildId,
                    UserId = s.UserId
                })
                .ToListAsync();

            var connString = db.Database.GetDbConnection().ConnectionString;
            var copyHelper = new PostgreSQLCopyHelper<GuildUser>("public", "guild_users")
                .MapInteger("guild_id", x => x.GuildId)
                .MapInteger("user_id", x => x.UserId);

            await using var connection = new NpgsqlConnection(connString);
            connection.Open();

            await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.guild_users WHERE guild_id = {existingGuild.GuildId};", connection);
            await deleteCurrentArtists.ExecuteNonQueryAsync().ConfigureAwait(false);

            await copyHelper.SaveAllAsync(connection, users).ConfigureAwait(false);

            Log.Information("Stored guild users for guild with id {guildId}", existingGuild.GuildId);
        }

        public async Task AddUserToGuild(IGuild guild, User user)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var existingGuild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    TitlesEnabled = true,
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                Log.Information("Added guild {guildName} to database", guild.Name);

                var guildId = db.Guilds.First(f => f.DiscordGuildId == guild.Id).GuildId;

                await db.GuildUsers.AddAsync(new GuildUser
                {
                    GuildId = guildId,
                    UserId = user.UserId
                });

                Log.Information("Added user {userId} to guild {guildName}", user.UserId, guild.Name);
            }
            else if (!existingGuild.GuildUsers.Select(g => g.UserId).Contains(user.UserId))
            {
                await db.GuildUsers.AddAsync(new GuildUser
                {
                    GuildId = existingGuild.GuildId,
                    UserId = user.UserId
                });

                Log.Information("Added user {userId} to guild {guildName}", user.UserId, guild.Name);
            }

            await db.SaveChangesAsync();
        }

        public async Task RemoveUserFromGuild(SocketGuildUser user)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var userThatLeft = await db.Users
                .Include(i => i.GuildUsers)
                .FirstOrDefaultAsync(f => f.DiscordUserId == user.Id);

            if (userThatLeft == null)
            {
                return;
            }

            var guild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == user.Guild.Id);

            if (guild != null && guild.GuildUsers.Select(g => g.UserId).Contains(userThatLeft.UserId))
            {
                var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == userThatLeft.UserId && f.GuildId == guild.GuildId);

                if (guildUser != null)
                {
                    db.GuildUsers.Remove(guildUser);

                    Log.Information("Removed user {userId} from guild {guildName}", userThatLeft.UserId, guild.Name);

                    await db.SaveChangesAsync();
                }
                else
                {
                    Log.Warning("Tried removing user {userId} from guild {guildName}, but user was not stored in guild", userThatLeft.UserId, guild.Name);
                }
            }
        }

        public async Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.Id).ToList();

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId)
                && (w.LastIndexed == null || w.LastUpdated == null))
                .ToListAsync();
        }

        public async Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.Id).ToList();

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId)
                    && w.LastIndexed != null)
                .CountAsync();
        }

        public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
