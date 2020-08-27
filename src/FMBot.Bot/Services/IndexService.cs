using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQLCopyHelper;

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
            await this._globalIndexService.IndexUser(user);
        }

        public void IndexGuild(IReadOnlyList<User> users)
        {
            Console.WriteLine($"Starting artist update for {users.Count} users");

            this._userIndexQueue.Publish(users.ToList());
        }

        public async Task IndexUser(User user)
        {
            Console.WriteLine($"Starting index for {user.UserNameLastFM}");

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

            Console.WriteLine($"Stored guild users");
        }

        public async Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.Id).ToList();

            var tooRecent = DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId)
                && (w.LastIndexed == null || w.LastIndexed <= tooRecent))
                .ToListAsync();
        }

        public async Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.Id).ToList();

            var indexCooldown = DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId)
                    && w.LastIndexed != null && w.LastIndexed >= indexCooldown)
                .CountAsync();
        }
    }
}
