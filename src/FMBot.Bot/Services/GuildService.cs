using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
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
            var user = await context.Guild.GetUserAsync(context.Client.CurrentUser.Id);
            return user.GuildPermissions;
        }


        // Get user from guild with searchvalue
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue)
        {
            var users = await context.Guild.GetUsersAsync();

            if (searchValue.Length > 3)
            {
                var id = searchValue.Trim('@', '!', '<', '>');
                var filteredUsers = users.Where(f =>
                    f.Id.ToString() == id || f.Nickname == searchValue || f.Username == searchValue);

                var user = filteredUsers.FirstOrDefault();

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

            var userIds = users.Select(s => s.Id).ToList();

            var usersObject = this.db.Users
                .Where(w => userIds.Contains(w.DiscordUserId))
                .Select(s =>
                    new UserExportModel(
                        s.DiscordUserId,
                        "Username temporarily not visible", //users.First(f => f.Id.ToString() == s.DiscordUserID).ToString() https://github.com/dotnet/efcore/issues/18179
                        s.UserNameLastFM));

            return usersObject.ToList();
        }

        public async Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, ChartType chartType)
        {
            var existingGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = chartTimePeriod,
                    ChartType = chartType,
                    Name = guild.Name,
                    TitlesEnabled = true
                };

                this.db.Guilds.Add(newGuild);

                await this.db.SaveChangesAsync();
            }
        }

        public async Task SetGuildReactionsAsync(IGuild guild, string[] reactions)
        {
            var existingGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    ChartType = ChartType.embedmini,
                    EmoteReactions = reactions,
                    Name = guild.Name
                };

                this.db.Guilds.Add(newGuild);

                await this.db.SaveChangesAsync();
            }
            else
            {
                existingGuild.EmoteReactions = reactions;
                existingGuild.Name = guild.Name;

                this.db.Entry(existingGuild).State = EntityState.Modified;

                await this.db.SaveChangesAsync();
            }
        }

        public async Task SetGuildPrefixAsync(IGuild guild, string prefix)
        {
            var existingGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    ChartType = ChartType.embedmini,
                    Name = guild.Name,
                    Prefix = prefix
                };

                this.db.Guilds.Add(newGuild);

                await this.db.SaveChangesAsync();
            }
            else
            {
                existingGuild.Prefix = prefix;
                existingGuild.Name = guild.Name;

                this.db.Entry(existingGuild).State = EntityState.Modified;

                await this.db.SaveChangesAsync();
            }
        }

        public async Task<DateTime?> GetGuildIndexTimestampAsync(IGuild guild)
        {
            var existingGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            return existingGuild?.LastIndexed;
        }

        public async Task UpdateGuildIndexTimestampAsync(IGuild guild, DateTime? timestamp = null)
        {
            var existingGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    ChartType = ChartType.embedmini,
                    Name = guild.Name,
                    TitlesEnabled = true,
                    LastIndexed = timestamp ?? DateTime.UtcNow
                };

                this.db.Guilds.Add(newGuild);

                await this.db.SaveChangesAsync();
            }
            else
            {
                existingGuild.LastIndexed = timestamp ?? DateTime.UtcNow;
                existingGuild.Name = guild.Name;

                this.db.Entry(existingGuild).State = EntityState.Modified;

                await this.db.SaveChangesAsync();
            }
        }

        public bool ValidateReactions(string[] emoteString)
        {
            foreach (var emote in emoteString)
            {
                if (emote.Length == 2)
                {
                    try
                    {
                        var unused = new Emoji(emote);
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        Emote.Parse(emote);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        public async Task AddReactionsAsync(IUserMessage message, IGuild guild)
        {
            var guildId = guild.Id.ToString();

            var dbGuild = await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (dbGuild?.EmoteReactions == null || !dbGuild.EmoteReactions.Any())
            {
                return;
            }

            foreach (var emoteString in dbGuild.EmoteReactions)
            {
                if (emoteString.Length == 2)
                {
                    var emote = new Emoji(emoteString);
                    await message.AddReactionAsync(emote);
                }
                else
                {
                    var emote = Emote.Parse(emoteString);
                    await message.AddReactionAsync(emote);
                }
            }
        }

        public async Task AddGuildAsync(SocketGuild guild)
        {
            var guildId = guild.Id.ToString();

            var newGuild = new Guild
            {
                DiscordGuildId = guild.Id,
                ChartTimePeriod = ChartTimePeriod.Monthly,
                ChartType = ChartType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true
            };

            this.db.Guilds.Add(newGuild);

            await this.db.SaveChangesAsync();
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            return await this.db.Guilds.FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id) != null;
        }

        public async Task<int> GetTotalGuildCountAsync()
        {
            return await this.db.Guilds.CountAsync();
        }
    }
}
