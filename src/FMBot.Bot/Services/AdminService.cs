using System;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    internal class AdminService
    {
        public async Task<bool> HasCommandAccessAsync(IUser discordUser, UserType userType)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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


        public async Task<bool> SetUserTypeAsync(ulong discordUserID, UserType userType)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.UserType = userType;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> AddUserToBlacklistAsync(ulong discordUserID)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.Blacklisted = true;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveUserFromBlacklistAsync(ulong discordUserID)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.Blacklisted = false;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
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
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('users', 'user_id'), (SELECT MAX(user_id) FROM users)+1);");
            Console.WriteLine("User key value has been fixed.");

            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('friends', 'friend_id'), (SELECT MAX(friend_id) FROM friends)+1);");
            Console.WriteLine("Friend key value has been fixed.");

            await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('guilds', 'guild_id'), (SELECT MAX(guild_id) FROM guilds)+1);");
            Console.WriteLine("Guild key value has been fixed.");
        }
    }
}
