using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FMBot.Services
{
    public class GuildService
    {
        private readonly FMBotDbContext db = new FMBotDbContext();

        // Message is in dm?
        public bool CheckIfDM(ICommandContext context)
        {
            return context.Guild == null;
        }


        // Get user from guild with ID
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, ulong id)
        {
            return await context.Guild.GetUserAsync(id).ConfigureAwait(false);
        }


        public async Task<GuildPermissions> CheckSufficientPermissionsAsync(ICommandContext context)
        {
            IGuildUser user = await context.Guild.GetUserAsync(context.Client.CurrentUser.Id).ConfigureAwait(false);
            return user.GuildPermissions;
        }


        // Get user from guild with searchvalue
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue)
        {
            IReadOnlyCollection<IGuildUser> users = await context.Guild.GetUsersAsync().ConfigureAwait(false);

            if (searchValue.Length > 3)
            {
                string id = searchValue.Trim(new char[] { '@', '!', '<', '>' });
                IEnumerable<IGuildUser> filteredUsers = users.Where(f => f.Id.ToString() == id || f.Nickname == searchValue || f.Username == searchValue);

                IGuildUser user = filteredUsers.FirstOrDefault();

                if (user != null)
                {
                    return user;
                }
            }

            return null;
        }


        // Get all guild users
        public async Task<Dictionary<string, string>> FindAllUsersFromGuildAsync(ICommandContext context)
        {
            IReadOnlyCollection<IGuildUser> users = await context.Guild.GetUsersAsync().ConfigureAwait(false);
            Dictionary<string, string> userList = new Dictionary<string, string>();

            foreach (IGuildUser user in users)
            {
                var userId = user.Id.ToString();
                User fmBotUser = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == userId);

                if (fmBotUser != null)
                {
                    userList.Add(user.Nickname ?? user.Username, fmBotUser.UserNameLastFM);
                }
            }

            return userList;
        }

        public async Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, ChartType chartType)
        {
            string guildId = guild.Id.ToString();
            Guild existingGuild = await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guildId).ConfigureAwait(false);

            if (existingGuild == null)
            {
                Guild newGuild = new Guild
                {
                    DiscordGuildID = guildId,
                    ChartTimePeriod = chartTimePeriod,
                    ChartType = chartType,
                    Name = guild.Name,
                    TitlesEnabled = true,
                };

                db.Guilds.Add(newGuild);

                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddGuildAsync(SocketGuild guild)
        {
            string guildId = guild.Id.ToString();

            Guild newGuild = new Guild
            {
                DiscordGuildID = guildId,
                ChartTimePeriod = ChartTimePeriod.Monthly,
                ChartType = ChartType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true,
            };

            db.Guilds.Add(newGuild);

            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            return await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guild.Id.ToString()).ConfigureAwait(false) != null;
        }
    }
}
