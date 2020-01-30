using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
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
            return await context.Guild.GetUserAsync(id);
        }


        public async Task<GuildPermissions> CheckSufficientPermissionsAsync(ICommandContext context)
        {
            IGuildUser user = await context.Guild.GetUserAsync(context.Client.CurrentUser.Id);
            return user.GuildPermissions;
        }


        // Get user from guild with searchvalue
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue)
        {
            IReadOnlyCollection<IGuildUser> users = await context.Guild.GetUsersAsync();

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
        public async Task<List<UserExportModel>> FindAllUsersFromGuildAsync(ICommandContext context)
        {
            var users = await context.Guild.GetUsersAsync();

            var userIds = users.Select(s => s.Id.ToString()).ToList();

            var usersObject = this.db.Users
                .Where(w => userIds.Contains(w.DiscordUserID))
                .Select(s =>
                    new UserExportModel(
                        s.DiscordUserID,
                        "Username temporarily not visible", //users.First(f => f.Id.ToString() == s.DiscordUserID).ToString() https://github.com/dotnet/efcore/issues/18179
                        s.UserNameLastFM));

            return usersObject.ToList();
        }

        public async Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, ChartType chartType)
        {
            string guildId = guild.Id.ToString();
            Guild existingGuild = await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guildId);

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

                await db.SaveChangesAsync();
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

            await db.SaveChangesAsync();
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            return await db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildID == guild.Id.ToString()) != null;
        }
    }
}
