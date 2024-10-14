using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services.Guild;

public class GuildDisabledCommandService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private static readonly ConcurrentDictionary<ulong, string[]> GuildToggledCommands = new();

    public GuildDisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public static void StoreDisabledCommands(string[] commands, ulong key)
    {
        if (GuildToggledCommands.ContainsKey(key))
        {
            if (commands == null)
            {
                RemoveDisabledCommands(key);
            }

            var oldDisabledCommands = GetToggledCommands(key);
            if (!GuildToggledCommands.TryUpdate(key, commands, oldDisabledCommands))
            {
                Log.Information($"Failed to update disabled guild commands {commands} with the key: {key} from the dictionary");
            }

            return;
        }

        if (!GuildToggledCommands.TryAdd(key, commands))
        {
            Log.Information($"Failed to add disabled guild commands {commands} with the key: {key} from the dictionary");
        }
    }


    public static string[] GetToggledCommands(ulong? key)
    {
        if (!key.HasValue)
        {
            return null;
        }

        return !GuildToggledCommands.ContainsKey(key.Value) ? null : GuildToggledCommands[key.Value];
    }


    private static void RemoveDisabledCommands(ulong key)
    {
        if (!GuildToggledCommands.ContainsKey(key))
        {
            return;
        }

        if (!GuildToggledCommands.TryRemove(key, out var removedDisabledCommands))
        {
            Log.Information($"Failed to remove custom disabled guild commands {removedDisabledCommands} with the key: {key} from the dictionary");
        }
    }


    public async Task LoadAllDisabledCommands()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
