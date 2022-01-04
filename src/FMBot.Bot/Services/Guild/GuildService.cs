using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services.Guild
{
    public class GuildService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;

        public GuildService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        // Message is in dm?
        public bool CheckIfDM(ICommandContext context)
        {
            return context.Guild == null;
        }

        public async Task<Persistence.Domain.Models.Guild> GetGuildAsync(ulong guildId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guildId);
        }

        public async Task<Persistence.Domain.Models.Guild> GetFullGuildAsync(ulong? discordGuildId = null, bool filterBots = true, bool enableCache = true)
        {
            if (discordGuildId == null)
            {
                return null;
            }

            var cacheKey = CacheKeyForGuild(discordGuildId.Value);

            var cachedGuildAvailable = this._cache.TryGetValue(cacheKey, out Persistence.Domain.Models.Guild guild);
            if (cachedGuildAvailable && enableCache)
            {
                return guild;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            guild = await db.Guilds
                .AsNoTracking()
                .Include(i => i.GuildBlockedUsers)
                    .ThenInclude(t => t.User)
                .Include(i => i.GuildUsers.Where(w => filterBots ? w.Bot != true : w.UserId != null))
                    .ThenInclude(t => t.User)
                .Include(i => i.Channels)
                .Include(i => i.Webhooks)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            this._cache.Set(cacheKey, guild, TimeSpan.FromMinutes(5));

            return guild;
        }

        public async Task RemoveGuildFromCache(ulong discordGuildId)
        {
            this._cache.Remove(CacheKeyForGuild(discordGuildId));
        }

        private static string CacheKeyForGuild(ulong discordGuildId)
        {
            return $"guild-full-{discordGuildId}";
        }

        public static IEnumerable<GuildUser> FilterGuildUsersAsync(Persistence.Domain.Models.Guild guild)
        {
            var guildUsers = guild.GuildUsers.ToList();
            if (guild.ActivityThresholdDays.HasValue)
            {
                guildUsers = guildUsers.Where(w =>
                    w.User.LastUsed != null &&
                    w.User.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                    .ToList();
            }
            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
            {
                guildUsers = guildUsers.Where(w =>
                    !guild.GuildBlockedUsers
                        .Where(wh => wh.BlockedFromWhoKnows)
                        .Select(s => s.UserId).Contains(w.UserId))
                    .ToList();
            }

            return guildUsers.ToList();
        }

        public static async Task<GuildPermissions> GetGuildPermissionsAsync(ICommandContext context)
        {
            var socketCommandContext = (SocketCommandContext) context;
            var guildUser = await context.Guild.GetUserAsync(socketCommandContext.Client.CurrentUser.Id);
            return guildUser.GuildPermissions;
        }

        public async Task<GuildUser> GetUserFromGuild(Persistence.Domain.Models.Guild guild, int userId)
        {
            return guild.GuildUsers
                .FirstOrDefault(f => f.UserId == userId);
        }

        public async Task<GuildUser> GetUserFromGuild(ulong discordGuildId, int userId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var guild = await db.Guilds
                .AsQueryable()
                .Include(i => i.GuildUsers)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild?.GuildUsers != null && guild.GuildUsers.Any())
            {
                return guild.GuildUsers.FirstOrDefault(f => f.UserId == userId);
            }

            return null;
        }

        // Get all guild users
        public async Task<List<UserExportModel>> FindAllUsersFromGuildAsync(IGuild discordGuild)
        {
            var users = await discordGuild.GetUsersAsync();

            var userIds = users.Select(s => s.Id).ToList();

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var usersObject = db.Users
                .AsNoTracking()
                .Where(w => userIds.Contains(w.DiscordUserId))
                .Select(s =>
                    new UserExportModel(
                        s.DiscordUserId.ToString(),
                        s.UserNameLastFM));

            return usersObject.ToList();
        }

        public async Task StaleGuildLastIndexedAsync(IGuild discordGuild)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

            existingGuild.Name = discordGuild.Name;
            existingGuild.LastIndexed = null;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);
        }

        public async Task ChangeGuildSettingAsync(IGuild discordGuild, Persistence.Domain.Models.Guild newGuildSettings)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    Name = discordGuild.Name,
                    TitlesEnabled = true
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.Name = discordGuild.Name;
                existingGuild.FmEmbedType = newGuildSettings.FmEmbedType;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }

            await RemoveGuildFromCache(discordGuild.Id);
        }

        public async Task SetGuildReactionsAsync(IGuild discordGuild, string[] reactions)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    EmoteReactions = reactions,
                    Name = discordGuild.Name
                };

                await db.Guilds.AddAsync(newGuild);
            }
            else
            {
                existingGuild.EmoteReactions = reactions;
                existingGuild.Name = discordGuild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

        }

        public async Task<bool?> ToggleSupporterMessagesAsync(IGuild discordGuild)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    Name = discordGuild.Name,
                    DisableSupporterMessages = true
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);

                return true;
            }
            else
            {
                existingGuild.Name = discordGuild.Name;
                if (existingGuild.DisableSupporterMessages == true)
                {
                    existingGuild.DisableSupporterMessages = false;
                }
                else
                {
                    existingGuild.DisableSupporterMessages = true;
                }

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);

                return existingGuild.DisableSupporterMessages;
            }
        }

        public async Task<bool?> ToggleCrownsAsync(IGuild discordGuild)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

            existingGuild.Name = discordGuild.Name;

            if (existingGuild.CrownsDisabled == true)
            {
                existingGuild.CrownsDisabled = false;
            }
            else
            {
                existingGuild.CrownsDisabled = true;
            }

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return existingGuild.CrownsDisabled;
        }

        public async Task<bool> SetWhoKnowsActivityThresholdDaysAsync(IGuild discordGuild, int? days)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = discordGuild.Name;
            existingGuild.ActivityThresholdDays = days;
            existingGuild.CrownsActivityThresholdDays = days;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        public async Task<bool> SetCrownActivityThresholdDaysAsync(IGuild discordGuild, int? days)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = discordGuild.Name;
            existingGuild.CrownsActivityThresholdDays = days;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        public async Task<bool> SetMinimumCrownPlaycountThresholdAsync(IGuild discordGuild, int? playcount)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = discordGuild.Name;
            existingGuild.CrownsMinimumPlaycountThreshold = playcount;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        public async Task<bool> BlockGuildUserAsync(IGuild discordGuild, int userId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser != null)
            {
                existingBlockedUser.BlockedFromWhoKnows = true;
                existingBlockedUser.BlockedFromCrowns = true;

                db.Entry(existingBlockedUser).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);

                return true;
            }

            var blockedGuildUserToAdd = new GuildBlockedUser
            {
                GuildId = existingGuild.GuildId,
                UserId = userId,
                BlockedFromCrowns = true,
                BlockedFromWhoKnows = true
            };

            await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

            Log.Information("Added blocked user {userId} to guild {guildName}", userId, discordGuild.Name);

            return true;
        }

        public async Task<bool> CrownBlockGuildUserAsync(IGuild discordGuild, int userId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser != null)
            {
                existingBlockedUser.BlockedFromCrowns = true;

                db.Entry(existingBlockedUser).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);

                return true;
            }

            var blockedGuildUserToAdd = new GuildBlockedUser
            {
                GuildId = existingGuild.GuildId,
                UserId = userId,
                BlockedFromCrowns = true
            };

            await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

            Log.Information("Added crownblocked user {userId} to guild {guildName}", userId, discordGuild.Name);

            return true;
        }

        public async Task<bool> UnBlockGuildUserAsync(IGuild discordGuild, int userId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser == null)
            {
                return true;
            }

            db.GuildBlockedUsers.Remove(existingBlockedUser);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            Log.Information("Removed blocked user {userId} from guild {guildName}", userId, discordGuild.Name);

            return true;
        }

        public async Task SetGuildPrefixAsync(IGuild discordGuild, string prefix)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    Name = discordGuild.Name,
                    Prefix = prefix
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);
            }
            else
            {
                existingGuild.Prefix = prefix;
                existingGuild.Name = discordGuild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);
            }
        }

        public async Task SetGuildWhoKnowsWhitelistRoleAsync(IGuild discordGuild, ulong? roleId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    Name = discordGuild.Name,
                    WhoKnowsWhitelistRoleId = roleId,
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);
            }
            else
            {
                existingGuild.WhoKnowsWhitelistRoleId = roleId;
                existingGuild.Name = discordGuild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);
            }
        }

        public async Task<string[]> GetDisabledCommandsForGuild(IGuild discordGuild)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            return existingGuild?.DisabledCommands;
        }

        public async Task<string[]> AddGuildDisabledCommandAsync(IGuild discordGuild, string command)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    TitlesEnabled = true,
                    Name = discordGuild.Name,
                    DisabledCommands = new[] { command }
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);

                return newGuild.DisabledCommands;
            }

            if (existingGuild.DisabledCommands != null && existingGuild.DisabledCommands.Length > 0)
            {
                var newDisabledCommands = existingGuild.DisabledCommands;
                Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
                newDisabledCommands[^1] = command;
                existingGuild.DisabledCommands = newDisabledCommands;
            }
            else
            {
                existingGuild.DisabledCommands = new[] { command };
            }

            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return existingGuild.DisabledCommands;
        }

        public async Task<string[]> RemoveGuildDisabledCommandAsync(IGuild discordGuild, string command)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            existingGuild.DisabledCommands = existingGuild.DisabledCommands.Where(w => !w.Contains(command)).ToArray();

            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return existingGuild.DisabledCommands;
        }

        public async Task<string[]> GetDisabledCommandsForChannel(IChannel discordChannel)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

            return existingChannel?.DisabledCommands;
        }

        public async Task<string[]> AddChannelDisabledCommandAsync(IChannel discordChannel, int guildId, string command, ulong discordGuildId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

            if (existingChannel == null)
            {
                var newChannel = new Channel
                {
                    DiscordChannelId = discordChannel.Id,
                    Name = discordChannel.Name,
                    GuildId = guildId,
                    DisabledCommands = new[] { command }
                };

                await db.Channels.AddAsync(newChannel);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuildId);

                return newChannel.DisabledCommands;
            }

            if (existingChannel.DisabledCommands != null && existingChannel.DisabledCommands.Length > 0)
            {
                var newDisabledCommands = existingChannel.DisabledCommands;
                Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
                newDisabledCommands[^1] = command;
                existingChannel.DisabledCommands = newDisabledCommands;
            }
            else
            {
                existingChannel.DisabledCommands = new[] { command };
            }

            existingChannel.Name = existingChannel.Name;

            db.Entry(existingChannel).State = EntityState.Modified;

            await RemoveGuildFromCache(discordGuildId);

            await db.SaveChangesAsync();

            return existingChannel.DisabledCommands;
        }

        public async Task<string[]> RemoveChannelDisabledCommandAsync(IChannel discordChannel, string command, ulong discordGuildId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

            existingChannel.DisabledCommands = existingChannel.DisabledCommands.Where(w => !w.Contains(command)).ToArray();

            existingChannel.Name = discordChannel.Name;

            db.Entry(existingChannel).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return existingChannel.DisabledCommands;
        }

        public async Task<int?> GetChannelCooldown(ulong discordChannelId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

            return existingChannel?.FmCooldown;
        }

        public async Task<int?> SetChannelCooldownAsync(IChannel discordChannel, int guildId, int? cooldown, ulong discordGuildId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

            if (existingChannel == null)
            {
                var newChannel = new Channel
                {
                    DiscordChannelId = discordChannel.Id,
                    Name = discordChannel.Name,
                    GuildId = guildId,
                    FmCooldown = cooldown
                };

                await db.Channels.AddAsync(newChannel);

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuildId);

                return newChannel.FmCooldown;
            }

            existingChannel.FmCooldown = cooldown;

            existingChannel.Name = existingChannel.Name;

            db.Entry(existingChannel).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return existingChannel.FmCooldown;
        }

        public async Task<DateTime?> GetGuildIndexTimestampAsync(IGuild discordGuild)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            return existingGuild?.LastIndexed;
        }

        public async Task UpdateGuildIndexTimestampAsync(IGuild discordGuild, DateTime? timestamp = null)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = discordGuild.Id,
                    Name = discordGuild.Name,
                    TitlesEnabled = true,
                    LastIndexed = timestamp ?? DateTime.UtcNow
                };

                await db.Guilds.AddAsync(newGuild);

                await RemoveGuildFromCache(discordGuild.Id);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.LastIndexed = timestamp ?? DateTime.UtcNow;
                existingGuild.Name = discordGuild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                await RemoveGuildFromCache(discordGuild.Id);
            }
        }

        public bool ValidateReactions(string[] emoteString)
        {
            foreach (var emote in emoteString)
            {
                if (emote.Length is 2 or 3)
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
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var dbGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (dbGuild?.EmoteReactions == null || !dbGuild.EmoteReactions.Any())
            {
                return;
            }

            foreach (var emoteString in dbGuild.EmoteReactions)
            {
                if (emoteString.Length is 2 or 3)
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

        public async Task RemoveGuildAsync(ulong discordGuildId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var guild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

            if (guild != null)
            {
                db.Guilds.Remove(guild);
                await db.SaveChangesAsync();
                await RemoveGuildFromCache(discordGuildId);
            }
        }

        public async Task<int> GetTotalGuildCountAsync()
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.Guilds
                .AsNoTracking()
                .CountAsync();
        }

        public Persistence.Domain.Models.Guild SetSettings(Persistence.Domain.Models.Guild guildSettings, string[] extraOptions)
        {
            if (extraOptions == null)
            {
                guildSettings.FmEmbedType = null;
                return guildSettings;
            }

            extraOptions = extraOptions.Select(s => s.ToLower()).ToArray();
            if (extraOptions.Contains("embedfull") || extraOptions.Contains("ef"))
            {
                guildSettings.FmEmbedType = FmEmbedType.EmbedFull;
            }
            else if (extraOptions.Contains("embedtiny"))
            {
                guildSettings.FmEmbedType = FmEmbedType.EmbedTiny;
            }
            else if (extraOptions.Contains("textmini") || extraOptions.Contains("tm"))
            {
                guildSettings.FmEmbedType = FmEmbedType.TextMini;
            }
            else if (extraOptions.Contains("textfull") || extraOptions.Contains("tf"))
            {
                guildSettings.FmEmbedType = FmEmbedType.TextFull;
            }
            else if (extraOptions.Contains("embedmini") || extraOptions.Contains("em"))
            {
                guildSettings.FmEmbedType = FmEmbedType.EmbedMini;
            }
            else
            {
                guildSettings.FmEmbedType = null;
            }

            return guildSettings;
        }
    }
}
