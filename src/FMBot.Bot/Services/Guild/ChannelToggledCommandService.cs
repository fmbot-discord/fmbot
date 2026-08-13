using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.Guild;

public class ChannelToggledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
{
    private static readonly ConcurrentDictionary<ulong, string[]> ChannelDisabledCommands = new();

    private static void StoreToggledCommands(string[] commands, ulong key)
    {
        if (commands == null)
        {
            RemoveToggledCommands(key);
            return;
        }

        ChannelDisabledCommands[key] = commands;
    }

    public static string[] GetToggledCommands(ulong? key)
    {
        if (!key.HasValue)
        {
            return null;
        }

        return ChannelDisabledCommands.GetValueOrDefault(key.Value);
    }


    private static void RemoveToggledCommands(ulong key)
    {
        ChannelDisabledCommands.TryRemove(key, out _);
    }

    public async Task LoadAllToggledCommands()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var channels = await db
            .Channels
            .AsQueryable()
            .Where(w => w.DisabledCommands != null)
            .ToListAsync();

        foreach (var channel in channels.Where(w => w.DisabledCommands.Length > 0))
        {
            StoreToggledCommands(channel.DisabledCommands, channel.DiscordChannelId);
        }
    }

    public async Task RemoveToggledCommandsForGuild(ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var guild = await db
            .Guilds
            .Include(i => i.Channels)
            .Where(w => w.DiscordGuildId == discordGuildId && w.Channels != null && w.Channels.Any())
            .FirstOrDefaultAsync();

        if (guild != null)
        {
            foreach (var channel in guild.Channels)
            {
                RemoveToggledCommands(channel.DiscordChannelId);
            }
        }
    }

    public async Task ReloadToggledCommands(ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var guild = await db
            .Guilds
            .Include(i => i.Channels)
            .Where(w => w.DiscordGuildId == discordGuildId && w.Channels != null && w.Channels.Any())
            .FirstOrDefaultAsync();

        if (guild != null)
        {
            foreach (var channel in guild.Channels)
            {
                StoreToggledCommands(channel.DisabledCommands, channel.DiscordChannelId);
            }
        }
    }
}
