using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Linq;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.Guild;

public class DisabledChannelService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private static readonly ConcurrentDictionary<ulong, bool> DisabledChannels = new();

    public DisabledChannelService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    private static void AddDisabledChannel(ulong key)
    {
        if (DisabledChannels.ContainsKey(key))
        {
            return;
        }

        DisabledChannels.TryAdd(key, true);
    }

    public static bool GetDisabledChannel(ulong? key)
    {
        return key.HasValue && DisabledChannels.ContainsKey(key.Value);
    }

    private static void RemoveDisabledChannel(ulong key)
    {
        if (!DisabledChannels.ContainsKey(key))
        {
            return;
        }

        DisabledChannels.TryRemove(key, out _);
    }

    public async Task LoadAllDisabledChannels()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var channels = await db
            .Channels
            .AsQueryable()
            .Where(w => w.BotDisabled == true)
            .ToListAsync();

        foreach (var channel in channels)
        {
            AddDisabledChannel(channel.DiscordChannelId);
        }
    }

    public async Task RemoveDisabledChannelsForGuild(ulong discordGuildId)
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
                RemoveDisabledChannel(channel.DiscordChannelId);
            }
        }
    }

    public async Task ReloadDisabledChannels(ulong discordGuildId)
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
                if (channel.BotDisabled == true)
                {
                    AddDisabledChannel(channel.DiscordChannelId);
                }
                else
                {
                    RemoveDisabledChannel(channel.DiscordChannelId);
                }
            }
        }
    }

}
