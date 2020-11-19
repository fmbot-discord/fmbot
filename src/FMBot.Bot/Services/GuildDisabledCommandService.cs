using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services
{
    public class GuildDisabledCommandService : IGuildDisabledCommandService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private static readonly ConcurrentDictionary<ulong, string[]> GuildDisabledCommands =
            new ConcurrentDictionary<ulong, string[]>();

        public GuildDisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public void StoreDisabledCommands(string[] commands, ulong key)
        {
            if (GuildDisabledCommands.ContainsKey(key))
            {
                if (commands == null)
                {
                    RemoveDisabledCommands(key);
                }

                var oldDisabledCommands = GetDisabledCommands(key);
                if (!GuildDisabledCommands.TryUpdate(key, commands, oldDisabledCommands))
                {
                    Log.Information($"Failed to update disabled guild commands {commands} with the key: {key} from the dictionary");
                }

                return;
            }

            if (!GuildDisabledCommands.TryAdd(key, commands))
            {
                Log.Information($"Failed to add disabled guild commands {commands} with the key: {key} from the dictionary");
            }
        }


        public string[] GetDisabledCommands(ulong? key)
        {
            if (!key.HasValue)
            {
                return null;
            }

            return !GuildDisabledCommands.ContainsKey(key.Value) ? null : GuildDisabledCommands[key.Value];
        }


        public void RemoveDisabledCommands(ulong key)
        {
            if (!GuildDisabledCommands.ContainsKey(key))
            {
                return;
            }

            if (!GuildDisabledCommands.TryRemove(key, out var removedDisabledCommands))
            {
                Log.Information($"Failed to remove custom disabled guild commands {removedDisabledCommands} with the key: {key} from the dictionary");
            }
        }


        public async Task LoadAllDisabledCommands()
        {
            await using var db = this._contextFactory.CreateDbContext();
            var servers = await db.Guilds
                .Where(w => w.DisabledCommands != null && w.DisabledCommands.Length > 0)
                .ToListAsync();
            foreach (var server in servers)
            {
                StoreDisabledCommands(server.DisabledCommands, server.DiscordGuildId);
            }
        }
    }
}
