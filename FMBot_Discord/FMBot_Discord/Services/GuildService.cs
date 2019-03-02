using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

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
            return await context.Guild.GetUserAsync(id).ConfigureAwait(false);
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

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Guild added to database.")).ConfigureAwait(false);
            }



            // IGuildUser ServerUser = (IGuildUser)Context.Message.Author;
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

            await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Guild added to database.")).ConfigureAwait(false);
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            return await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guild.Id.ToString()).ConfigureAwait(false) != null;
        }
    }
}
