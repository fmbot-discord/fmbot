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
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public IndexService(IUserIndexQueue userIndexQueue, GlobalIndexService indexService, IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._userIndexQueue = userIndexQueue;
            this._userIndexQueue.UsersToIndex.SubscribeAsync(OnNextAsync);
            this._globalIndexService = indexService;
            this._contextFactory = contextFactory;
        }

        private async Task OnNextAsync(IndexUserQueueItem user)
        {
            await this._globalIndexService.IndexUser(user);
        }

        public void AddUsersToIndexQueue(IReadOnlyList<User> users)
        {
            this._userIndexQueue.Publish(users);
        }

        public async Task IndexUser(User user)
        {
            Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

            await this._globalIndexService.IndexUser(new IndexUserQueueItem(user.UserId));
        }

        public async Task StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers)
        {
            var userIds = discordGuildUsers.Select(s => s.Id).ToList();

            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
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
                    User = s
                })
                .ToListAsync();

            foreach (var user in users)
            {
                var discordUser = discordGuildUsers.First(f => f.Id == user.User.DiscordUserId);
                var name = discordUser.Nickname ?? discordUser.Username;
                user.UserName = name;
            }

            var connString = db.Database.GetDbConnection().ConnectionString;
            var copyHelper = new PostgreSQLCopyHelper<GuildUser>("public", "guild_users")
                .MapInteger("guild_id", x => x.GuildId)
                .MapInteger("user_id", x => x.UserId)
                .MapText("user_name", x => x.UserName);

            await using var connection = new NpgsqlConnection(connString);
            connection.Open();

            await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.guild_users WHERE guild_id = {existingGuild.GuildId};", connection);
            await deleteCurrentArtists.ExecuteNonQueryAsync().ConfigureAwait(false);

            await copyHelper.SaveAllAsync(connection, users).ConfigureAwait(false);

            Log.Information("Stored guild users for guild with id {guildId}", existingGuild.GuildId);
        }

        public async Task<GuildUser> GetOrAddUserToGuild(Guild guild, IGuildUser discordGuildUser, User user)
        {
            await using var db = this._contextFactory.CreateDbContext();

            try
            {
                if (!guild.GuildUsers.Select(g => g.UserId).Contains(user.UserId))
                {
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

                    await db.GuildUsers.AddAsync(guildUserToAdd);
                    await db.SaveChangesAsync();

                    db.Entry(guildUserToAdd).State = EntityState.Detached;

                    Log.Information("Added user {userId} to guild {guildName}", user.UserId, guild.Name);

                    guildUserToAdd.User = user;

                    return guildUserToAdd;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while attempting to add user {userId} to guild {guildId}", user.UserId, guild.GuildId);
                return new GuildUser
                {
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser.Nickname ?? discordGuildUser.Username
                };
            }

            return guild.GuildUsers.First(f => f.UserId == user.UserId);
        }


        public async Task UpdateUserName(GuildUser guildUser, IGuildUser discordGuildUser)
        {
            var discordName = discordGuildUser.Nickname ?? discordGuildUser.Username;

            if (guildUser.UserName != discordName)
            {
                await using var db = this._contextFactory.CreateDbContext();

                guildUser.UserName = discordName;

                db.GuildUsers.Update(guildUser);

                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateUserNameWithoutGuildUser(IGuildUser discordGuildUser, User user)
        {
            var discordName = discordGuildUser.Nickname ?? discordGuildUser.Username;

            await using var db = this._contextFactory.CreateDbContext();
            var guildUser = await db.GuildUsers
                .Include(i => i.Guild)
                .FirstOrDefaultAsync(f => f.UserId == user.UserId &&
                                          f.Guild.DiscordGuildId == discordGuildUser.GuildId);

            if (guildUser != null && guildUser.UserName != discordName)
            {
                guildUser.UserName = discordName;

                db.GuildUsers.Update(guildUser);

                await db.SaveChangesAsync();
            }
        }

        public async Task RemoveUserFromGuild(SocketGuildUser user)
        {
            await using var db = this._contextFactory.CreateDbContext();
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

        public async Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> discordGuildUsers)
        {
            var userIds = discordGuildUsers.Select(s => s.Id).ToList();

            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId)
                && (w.LastIndexed == null || w.LastUpdated == null))
                .ToListAsync();
        }

        public async Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers)
        {
            var userIds = discordGuildUsers.Select(s => s.Id).ToList();

            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId)
                    && w.LastIndexed != null)
                .CountAsync();
        }

        public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed)
        {
            await using var db = this._contextFactory.CreateDbContext();
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
