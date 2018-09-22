using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using System.Collections.Generic;
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
            var users = await context.Guild.GetUsersAsync();

            if (searchValue.Length > 3)
            {
                var filteredUsers = users.Where(f => f.Id.ToString().Contains(searchValue) || f.Nickname == searchValue || f.Username == searchValue);

                var user = filteredUsers.FirstOrDefault();

                if (user != null)
                {
                    return user;
                }
            }

            return null;
        }
    }
}
