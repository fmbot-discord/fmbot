using System;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services
{
    public class AdminService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;

        public AdminService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        public async Task<bool> HasCommandAccessAsync(IUser discordUser, UserType userType)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return false;
            }

            switch (user.UserType)
            {
                case UserType.Admin:
                    switch (userType)
                    {
                        case UserType.User:
                            return true;
                        case UserType.Admin:
                            return true;
                        default:
                            return false;
                    }
                case UserType.Owner:
                    switch (userType)
                    {
                        case UserType.User:
                            return true;
                        case UserType.Admin:
                            return true;
                        default:
                            return true;
                    }
                default:
                    switch (userType)
                    {
                        case UserType.User:
                            return true;
                        default:
                            return false;
                    }
            }
        }

        public async Task<bool> SetUserTypeAsync(ulong discordUserId, UserType userType)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return false;
            }

            user.UserType = userType;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> AddUserToBlocklistAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return false;
            }

            user.Blocked = true;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();

            this._cache.Remove("blocked-users");

            return true;
        }

        public async Task<bool> RemoveUserFromBlocklistAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return false;
            }

            user.Blocked = false;

            db.Entry(user).State = EntityState.Modified;

            this._cache.Remove("blocked-users");

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<BottedUser> GetBottedUserAsync(string lastFmUserName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.BottedUsers
                .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());
        }

        public string FormatBytes(long bytes)
        {
            string[] suffix = {"B", "KB", "MB", "GB", "TB"};
            int i;
            double dblSByte = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return string.Format("{0:0.##} {1}", dblSByte, suffix[i]);
        }

        public async Task FixValues()
        {
            await using var db = this._contextFactory.CreateDbContext();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('users', 'user_id'), (SELECT MAX(user_id) FROM users)+1);");
            Console.WriteLine("User key value has been fixed.");

            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('friends', 'friend_id'), (SELECT MAX(friend_id) FROM friends)+1);");
            Console.WriteLine("Friend key value has been fixed.");

            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('guilds', 'guild_id'), (SELECT MAX(guild_id) FROM guilds)+1);");
            Console.WriteLine("Guild key value has been fixed.");
        }
    }
}
