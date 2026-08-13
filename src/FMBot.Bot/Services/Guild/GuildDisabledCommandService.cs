using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.Guild;

public class GuildDisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
{
    private static readonly ConcurrentDictionary<ulong, string[]> GuildToggledCommands = new();

    public static void StoreDisabledCommands(string[] commands, ulong key)
    {
        if (commands == null)
        {
            RemoveDisabledCommands(key);
            return;
        }

        GuildToggledCommands[key] = commands;
    }


    public static string[] GetToggledCommands(ulong? key)
    {
        if (!key.HasValue)
        {
            return null;
        }

        return GuildToggledCommands.GetValueOrDefault(key.Value);
    }


    private static void RemoveDisabledCommands(ulong key)
    {
        GuildToggledCommands.TryRemove(key, out _);
    }


    public async Task LoadAllDisabledCommands()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var servers = await db.Guilds
            .Where(w => w.DisabledCommands != null)
            .ToListAsync();

        servers = servers
            .Where(w => w.DisabledCommands.Length > 0)
            .ToList();

        foreach (var server in servers)
        {
            StoreDisabledCommands(server.DisabledCommands, server.DiscordGuildId);
        }
    }

    public async Task ReloadDisabledCommands(ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var server = await db.Guilds
            .Where(w => w.DiscordGuildId == discordGuildId)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            RemoveDisabledCommands(discordGuildId);
        }
        else
        {
            StoreDisabledCommands(server.DisabledCommands, server.DiscordGuildId);
        }
    }
}
