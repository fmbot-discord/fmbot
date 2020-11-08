using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services
{
    public class DisabledCommandService : IDisabledCommandService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        /// <summary>
        ///     The <see cref="ConcurrentDictionary{TKey,TValue}" /> that contains all the disabled commands.
        /// </summary>
        private static readonly ConcurrentDictionary<ulong, string[]> ServerDisabledCommands =
            new ConcurrentDictionary<ulong, string[]>();

        public DisabledCommandService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        /// <inheritdoc />
        public void StoreDisabledCommands(string[] commands, ulong key)
        {
            if (ServerDisabledCommands.ContainsKey(key))
            {
                if (commands == null)
                {
                    RemoveDisabledCommands(key);
                }

                var oldDisabledCommands = GetDisabledCommands(key);
                if (!ServerDisabledCommands.TryUpdate(key, commands, oldDisabledCommands))
                {
                    Log.Information($"Failed to update disabled commands {commands} with the key: {key} from the dictionary");
                }

                return;
            }

            if (!ServerDisabledCommands.TryAdd(key, commands))
            {
                Log.Information($"Failed to add disabled commands {commands} with the key: {key} from the dictionary");
            }
        }


        /// <inheritdoc />
        public string[] GetDisabledCommands(ulong? key)
        {
            if (!key.HasValue)
            {
                return null;
            }

            return !ServerDisabledCommands.ContainsKey(key.Value) ? null : ServerDisabledCommands[key.Value];
        }


        /// <inheritdoc />
        public void RemoveDisabledCommands(ulong key)
        {
            if (!ServerDisabledCommands.ContainsKey(key))
            {
                return;
            }

            if (!ServerDisabledCommands.TryRemove(key, out var removedDisabledCommands))
            {
                Log.Information($"Failed to remove custom disabled commands {removedDisabledCommands} with the key: {key} from the dictionary");
            }
        }


        /// <inheritdoc />
        public async Task LoadAllDisabledCommands()
        {
            await using var db = this._contextFactory.CreateDbContext();
            var servers = await db.Guilds.Where(w => w.DisabledCommands != null).ToListAsync();
            foreach (var server in servers)
            {
                StoreDisabledCommands(server.DisabledCommands, server.DiscordGuildId);
            }
        }
    }
}
