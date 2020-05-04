using System.Threading.Tasks;
using Discord;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    internal class AdminService
    {
        private readonly FMBotDbContext _db;

        public AdminService(FMBotDbContext db)
        {
            this._db = db;
        }

        public async Task<bool> HasCommandAccessAsync(IUser discordUser, UserType userType)
        {
            var user = await this._db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

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
            var user = await this._db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.UserType = userType;

            this._db.Entry(user).State = EntityState.Modified;

            await this._db.SaveChangesAsync();

            return true;
        }


        public async Task<bool> AddUserToBlacklistAsync(ulong discordUserID)
        {
            var user = await this._db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.Blacklisted = true;

            this._db.Entry(user).State = EntityState.Modified;

            await this._db.SaveChangesAsync();

            return true;
        }


        public async Task<bool> RemoveUserFromBlacklistAsync(ulong discordUserID)
        {
            var user = await this._db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserID);

            if (user == null)
            {
                return false;
            }

            user.Blacklisted = false;

            this._db.Entry(user).State = EntityState.Modified;

            await this._db.SaveChangesAsync();

            return true;
        }

        public string FormatBytes(long bytes)
        {
            string[] Suffix = {"B", "KB", "MB", "GB", "TB"};
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }
    }
}
