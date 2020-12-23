using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services.Guild
{
    public class ChannelDisabledCommandService : IChannelDisabledCommandService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private static readonly ConcurrentDictionary<ulong, string[]> ChannelDisabledCommands =
            new ConcurrentDictionary<ulong, string[]>();

        public ChannelDisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public void StoreDisabledCommands(string[] commands, ulong key)
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


        public string[] GetDisabledCommands(ulong? key)
        {
            if (!key.HasValue)
            {
                return null;
            }

            return !ChannelDisabledCommands.ContainsKey(key.Value) ? null : ChannelDisabledCommands[key.Value];
        }


        public void RemoveDisabledCommands(ulong key)
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
            await using var db = this._contextFactory.CreateDbContext();
            var channels = await db
                .Channels.Where(w => w.DisabledCommands != null && w.DisabledCommands.Length > 0)
                .ToListAsync();
            foreach (var channel in channels)
            {
                StoreDisabledCommands(channel.DisabledCommands, channel.DiscordChannelId);
            }
        }
    }
}
