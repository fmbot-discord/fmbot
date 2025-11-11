using Discord.Commands;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Services
{
    public class ShortcutService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private static readonly ConcurrentDictionary<ulong, List<Shortcut>> UserShortcuts = new();
        private static readonly ConcurrentDictionary<ulong, List<Shortcut>> GuildShortcuts = new();

        public ShortcutService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task LoadAllShortcuts()
        {
            await LoadAllUserShortcuts();
            await LoadAllGuildShortcuts();
        }

        private async Task LoadAllUserShortcuts()
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var allShortcuts = await db.UserShortcuts
                .AsNoTracking()
                .Include(i => i.User)
                .ToListAsync();

            var shortcutsByUserId = allShortcuts
                .Where(w => SupporterService.IsSupporter(w.User.UserType))
                .GroupBy(u => u.User.DiscordUserId)
                .ToDictionary(g => g.Key, g => g.Select(s => new Shortcut { Input = s.Input, Output = s.Output }).ToList());

            UserShortcuts.Clear();
            foreach (var userShortcut in shortcutsByUserId)
            {
                UserShortcuts.TryAdd(userShortcut.Key, userShortcut.Value);
            }

            Log.Debug($"Loaded shortcuts for {shortcutsByUserId.Count} users into memory.");
        }

        private async Task LoadAllGuildShortcuts()
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var allShortcuts = await db.GuildShortcuts
                .AsNoTracking()
                .Include(i => i.Guild)
                .ToListAsync();

            var shortcutsByGuildId = allShortcuts
                .GroupBy(u => u.Guild.DiscordGuildId)
                .ToDictionary(g => g.Key, g => g.Select(s => new Shortcut { Input = s.Input, Output = s.Output }).ToList());

            GuildShortcuts.Clear();
            foreach (var guildShortcut in shortcutsByGuildId)
            {
                GuildShortcuts.TryAdd(guildShortcut.Key, guildShortcut.Value);
            }

            Log.Debug($"Loaded shortcuts for {shortcutsByGuildId.Count} guilds into memory.");
        }

        public async Task<List<UserShortcut>> GetUserShortcuts(User user)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            return await db.UserShortcuts
                .Where(w => w.UserId == user.UserId)
                .ToListAsync();
        }

        public async Task<UserShortcut> GetUserShortcut(int id)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            return await db.UserShortcuts
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task AddOrUpdateUserShortcut(User user, int id, string input, string output)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var existing = await db.UserShortcuts.FirstOrDefaultAsync(s => s.UserId == user.UserId && s.Id == id);

            if (existing != null)
            {
                existing.Input = input;
                existing.Output = output;
                existing.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            }
            else
            {
                db.UserShortcuts.Add(new UserShortcut
                {
                    UserId = user.UserId,
                    Input = input,
                    Output = output,
                    Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                });
            }

            await db.SaveChangesAsync();
            await UpdateShortcutsForUser(user);
        }

        public async Task<bool> RemoveUserShortcut(User user, string shortcut)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var toRemove = await db.UserShortcuts.FirstOrDefaultAsync(s => s.UserId == user.UserId && s.Input == shortcut);

            if (toRemove != null)
            {
                db.UserShortcuts.Remove(toRemove);
                await db.SaveChangesAsync();
                await UpdateShortcutsForUser(user);
                return true;
            }

            return false;
        }

        public async Task<List<GuildShortcut>> GetGuildShortcuts(Persistence.Domain.Models.Guild guild)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            return await db.GuildShortcuts
                .Where(w => w.GuildId == guild.GuildId)
                .ToListAsync();
        }

        public async Task AddOrUpdateGuildShortcut(Persistence.Domain.Models.Guild guild, int id, string input, string output)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var existing = await db.GuildShortcuts.FirstOrDefaultAsync(s => s.GuildId == guild.GuildId && s.Id == id);

            if (existing != null)
            {
                existing.Input = input;
                existing.Output = output;
                existing.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            }
            else
            {
                db.GuildShortcuts.Add(new GuildShortcut
                {
                    GuildId = guild.GuildId,
                    Input = input,
                    Output = output,
                    Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                });
            }

            await db.SaveChangesAsync();
            await UpdateShortcutsForGuild(guild);
        }


        public async Task<bool> RemoveGuildShortcut(Persistence.Domain.Models.Guild guild, string shortcut)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var toRemove = await db.GuildShortcuts.FirstOrDefaultAsync(s => s.GuildId == guild.GuildId && s.Input == shortcut);

            if (toRemove != null)
            {
                db.GuildShortcuts.Remove(toRemove);
                await db.SaveChangesAsync();
                await UpdateShortcutsForGuild(guild);
                return true;
            }

            return false;
        }

        private async Task UpdateShortcutsForUser(User user)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var userShortcuts = await db.UserShortcuts
                .AsNoTracking()
                .Where(s => s.UserId == user.UserId)
                .Select(s => new Shortcut { Input = s.Input, Output = s.Output })
                .ToListAsync();

            if (userShortcuts.Count != 0)
            {
                UserShortcuts[user.DiscordUserId] = userShortcuts;
            }
            else
            {
                if (UserShortcuts.TryRemove(user.DiscordUserId, out _))
                {
                    Log.Debug("Removed user {UserId} from shortcut cache as they have no shortcuts.", user.UserId);
                }
            }
        }

        private async Task UpdateShortcutsForGuild(Persistence.Domain.Models.Guild guild)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var guildShortcuts = await db.GuildShortcuts
                .AsNoTracking()
                .Where(s => s.GuildId == guild.GuildId)
                .Select(s => new Shortcut { Input = s.Input, Output = s.Output })
                .ToListAsync();

            if (guildShortcuts.Count != 0)
            {
                GuildShortcuts[guild.DiscordGuildId] = guildShortcuts;
            }
            else
            {
                if (GuildShortcuts.TryRemove(guild.DiscordGuildId, out _))
                {
                    Log.Debug("Removed guild {GuildId} from shortcut cache as it has no shortcuts.", guild.GuildId);
                }
            }
        }

        public static (Shortcut shortcut, string remainingArgs)? FindShortcut(ICommandContext context, string messageContent)
        {
            var userShortcuts = UserShortcuts.TryGetValue(context.User.Id, out var uShorts) ? uShorts : Enumerable.Empty<Shortcut>();
            var guildShortcuts = context.Guild != null && GuildShortcuts.TryGetValue(context.Guild.Id, out var gShorts)
                ? gShorts
                : Enumerable.Empty<Shortcut>();

            var bestMatch = userShortcuts
                .Concat(guildShortcuts)
                .Where(s => messageContent.StartsWith(s.Input, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Input.Length)
                .FirstOrDefault();

            if (bestMatch != null)
            {
                if (messageContent.Length == bestMatch.Input.Length ||
                    (messageContent.Length > bestMatch.Input.Length && messageContent[bestMatch.Input.Length] == ' '))
                {
                    var remainingArgs = messageContent[bestMatch.Input.Length..].Trim();
                    return (bestMatch, remainingArgs);
                }
            }

            return null;
        }

        public static async Task AddShortcutReaction(ShardedCommandContext context)
        {
            if (context.Message != null)
            {
                await context.Message.AddReactionAsync(EmojiProperties.Custom(DiscordConstants.Shortcut));
            }
        }
    }
}
