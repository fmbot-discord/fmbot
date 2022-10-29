using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.Services;

public class PrefixService : IPrefixService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;

    private static readonly ConcurrentDictionary<ulong, string> ServerPrefixes = new();

    public PrefixService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
    }

    public void StorePrefix(string prefix, ulong key)
    {
        if (ServerPrefixes.ContainsKey(key))
        {
            var oldPrefix = GetPrefix(key);
            if (!ServerPrefixes.TryUpdate(key, prefix, oldPrefix))
            {
                Log.Warning($"Failed to update custom prefix {prefix} with the key: {key} from the dictionary");
            }

            return;
        }

        if (!ServerPrefixes.TryAdd(key, prefix))
        {
            Log.Warning($"Failed to add custom prefix {prefix} with the key: {key} from the dictionary");
        }
    }


    public string GetPrefix(ulong? key)
    {
        if (!key.HasValue)
        {
            return this._botSettings.Bot.Prefix;
        }

        return !ServerPrefixes.ContainsKey(key.Value) ? this._botSettings.Bot.Prefix : ServerPrefixes[key.Value];
    }


    public void RemovePrefix(ulong key)
    {
        if (!ServerPrefixes.ContainsKey(key))
        {
            return;
        }

        if (!ServerPrefixes.TryRemove(key, out var removedPrefix))
        {
            Log.Warning($"Failed to remove custom prefix {removedPrefix} with the key: {key} from the dictionary");
        }
    }


    public async Task LoadAllPrefixes()
    {
        await using var db = this._contextFactory.CreateDbContext();
        var servers = await db.Guilds.Where(w => w.Prefix != null).ToListAsync();
        foreach (var server in servers)
        {
            StorePrefix(server.Prefix, server.DiscordGuildId);
        }
    }

    public async Task ReloadPrefix(ulong discordGuildId)
    {
        await using var db = this._contextFactory.CreateDbContext();
        var server = await db.Guilds
            .Where(w => w.DiscordGuildId == discordGuildId)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            RemovePrefix(discordGuildId);
        }
        else
        {
            StorePrefix(server.Prefix, server.DiscordGuildId);
        }
    }
}
