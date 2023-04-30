using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Linq;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services.Guild;

public class ChannelDisabledCommandService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private static readonly ConcurrentDictionary<ulong, string[]> ChannelDisabledCommands = new();

    public ChannelDisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    private static void StoreDisabledCommands(string[] commands, ulong key)
    {
        if (ChannelDisabledCommands.ContainsKey(key))
        {
            if (commands == null)
            {
                RemoveDisabledCommands(key);
            }

            var oldDisabledCommands = GetDisabledCommands(key);
            if (!ChannelDisabledCommands.TryUpdate(key, commands, oldDisabledCommands))
            {
                Log.Information($"Failed to update disabled channel commands {commands} with the key: {key} from the dictionary");
            }

            return;
        }

        if (!ChannelDisabledCommands.TryAdd(key, commands))
        {
            Log.Information($"Failed to add disabled channel commands {commands} with the key: {key} from the dictionary");
        }
    }

    public static string[] GetDisabledCommands(ulong? key)
    {
        if (!key.HasValue)
        {
            return null;
        }

        return !ChannelDisabledCommands.ContainsKey(key.Value) ? null : ChannelDisabledCommands[key.Value];
    }


    private static void RemoveDisabledCommands(ulong key)
    {
        if (!ChannelDisabledCommands.ContainsKey(key))
        {
            return;
        }

        if (!ChannelDisabledCommands.TryRemove(key, out var removedDisabledCommands))
        {
            Log.Information($"Failed to remove custom disabled channel commands {removedDisabledCommands} with the key: {key} from the dictionary");
        }
    }

    public async Task LoadAllDisabledCommands()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var channels = await db
            .Channels
            .AsQueryable()
            .Where(w => w.DisabledCommands != null)
            .ToListAsync();

        foreach (var channel in channels.Where(w => w.DisabledCommands.Length > 0))
        {
            StoreDisabledCommands(channel.DisabledCommands, channel.DiscordChannelId);
        }
    }

    public async Task RemoveDisabledCommandsForGuild(ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guild = await db
            .Guilds
            .Include(i => i.Channels)
            .Where(w => w.DiscordGuildId == discordGuildId && w.Channels != null && w.Channels.Any())
            .FirstOrDefaultAsync();

        if (guild != null)
        {
            foreach (var channel in guild.Channels)
            {
                RemoveDisabledCommands(channel.DiscordChannelId);
            }
        }
    }

    public async Task ReloadDisabledCommands(ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guild = await db
            .Guilds
            .Include(i => i.Channels)
            .Where(w => w.DiscordGuildId == discordGuildId && w.Channels != null && w.Channels.Any())
            .FirstOrDefaultAsync();

        if (guild != null)
        {
            foreach (var channel in guild.Channels)
            {
                StoreDisabledCommands(channel.DisabledCommands, channel.DiscordChannelId);
            }
        }
    }
}
