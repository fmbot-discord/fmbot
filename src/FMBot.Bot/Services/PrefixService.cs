using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        if (prefix == null)
        {
            RemovePrefix(key);
            return;
        }

        ServerPrefixes[key] = prefix;
    }


    public string GetPrefix(ulong? key)
    {
        if (!key.HasValue)
        {
            return this._botSettings.Bot.Prefix;
        }

        return ServerPrefixes.TryGetValue(key.Value, out var prefix) ? prefix : this._botSettings.Bot.Prefix;
    }


    public void RemovePrefix(ulong key)
    {
        ServerPrefixes.TryRemove(key, out _);
    }


    public async Task LoadAllPrefixes()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var servers = await db.Guilds.Where(w => w.Prefix != null).ToListAsync();
        foreach (var server in servers)
        {
            StorePrefix(server.Prefix, server.DiscordGuildId);
        }
    }

    public async Task ReloadPrefix(ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
