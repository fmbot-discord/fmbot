using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using System.Collections.Generic;
using System.Data.Entity;
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
            return (context.Guild == null);
        }


        // Get user from guild with ID
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, ulong id)
        {
            IGuildUser guildUser = await context.Guild.GetUserAsync(id);

            return guildUser;
        }


        // Get user from guild with searchvalue
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue)
        {
            IReadOnlyCollection<IGuildUser> users = await context.Guild.GetUsersAsync();

            if (searchValue.Length > 3)
            {
                string id = searchValue.Trim(new char[] { '@', '!', '<', '>' });
                var filteredUsers = users.Where(f => f.Id.ToString() == id || f.Nickname == searchValue || f.Username == searchValue);

                var user = filteredUsers.FirstOrDefault();

                if (user != null)
                {
                    return user;
                }
            }

            return null;
        }

        public async Task AddGuildAsync(SocketGuild guild)
        {
            db.Guilds.Add(new Guild
            {
                DiscordGuildID = guild.Id.ToString(),
                ChartTimePeriod = ChartTimePeriod.Monthly,
                ChartType = ChartType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true,
            });

            await db.SaveChangesAsync();
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            return await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guild.Id.ToString()) != null;
        }
    }
}
