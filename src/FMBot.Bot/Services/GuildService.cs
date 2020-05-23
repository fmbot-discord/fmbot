using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class GuildService : IGuildService
    {
        // Message is in dm?
        public bool CheckIfDM(ICommandContext context)
        {
            return context.Guild == null;
        }

        public async Task<Guild> GetGuildAsync(ulong guildId)
        {
            await using var db = new FMBotDbContext();
            return await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guildId);
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

            await using var db = new FMBotDbContext();
            var usersObject = db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId))
                .Select(s =>
                    new UserExportModel(
                        s.DiscordUserId.ToString(),
                        s.UserNameLastFM));

            return usersObject.ToList();
        }

        public async Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, FmEmbedType fmEmbedType)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = chartTimePeriod,
                    FmEmbedType = fmEmbedType,
                    Name = guild.Name,
                    TitlesEnabled = true
                };

                db.Guilds.Add(newGuild);

                await db.SaveChangesAsync();
            }
        }

        public async Task SetGuildReactionsAsync(IGuild guild, string[] reactions)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    EmoteReactions = reactions,
                    Name = guild.Name
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.EmoteReactions = reactions;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public async Task SetGuildPrefixAsync(IGuild guild, string prefix)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    Prefix = prefix
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.Prefix = prefix;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public async Task<string[]> GetDisabledCommandsForGuild(IGuild guild)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            return existingGuild?.DisabledCommands;
        }

        public async Task<string[]> AddDisabledCommandAsync(IGuild guild, string command)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    DisabledCommands = new[] { command }
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                return newGuild.DisabledCommands;
            }
            else
            {
                if (existingGuild.DisabledCommands != null && existingGuild.DisabledCommands.Length > 0)
                {
                    var newDisabledCommands = existingGuild.DisabledCommands;
                    Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
                    newDisabledCommands[^1] = command;
                    existingGuild.DisabledCommands = newDisabledCommands;
                }
                else
                {
                    existingGuild.DisabledCommands = new[] {command};
                }

                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                return existingGuild.DisabledCommands;
            }
        }

        public async Task<string[]> RemoveDisabledCommandAsync(IGuild guild, string command)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            existingGuild.DisabledCommands = existingGuild.DisabledCommands.Where(w => !w.Contains(command)).ToArray();

            existingGuild.Name = guild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingGuild.DisabledCommands;
        }

        public async Task<DateTime?> GetGuildIndexTimestampAsync(IGuild guild)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            return existingGuild?.LastIndexed;
        }

        public async Task UpdateGuildIndexTimestampAsync(IGuild guild, DateTime? timestamp = null)
        {
            await using var db = new FMBotDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    TitlesEnabled = true,
                    LastIndexed = timestamp ?? DateTime.UtcNow
                };

                db.Guilds.Add(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.LastIndexed = timestamp ?? DateTime.UtcNow;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
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
            await using var db = new FMBotDbContext();
            var dbGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

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
            var newGuild = new Guild
            {
                DiscordGuildId = guild.Id,
                ChartTimePeriod = ChartTimePeriod.Monthly,
                FmEmbedType = FmEmbedType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true
            };

            await using var db = new FMBotDbContext();
            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            await using var db = new FMBotDbContext();
            return await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id) != null;
        }

        public async Task<int> GetTotalGuildCountAsync()
        {
            await using var db = new FMBotDbContext();
            return await db.Guilds
                .AsQueryable()
                .CountAsync();
        }
    }
}
