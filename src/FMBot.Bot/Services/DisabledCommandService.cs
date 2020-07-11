using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Services
{
    public class DisabledCommandService : IDisabledCommandService
    {
        private readonly Logger.Logger _logger;

        /// <summary>
        ///     The <see cref="ConcurrentDictionary{TKey,TValue}" /> that contains all the disabled commands.
        /// </summary>
        private static readonly ConcurrentDictionary<ulong, string[]> ServerDisabledCommands =
            new ConcurrentDictionary<ulong, string[]>();

        public DisabledCommandService(Logger.Logger logger)
        {
            this._logger = logger;
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
                    this._logger.Log($"Failed to update disabled commands {commands} with the key: {key} from the dictionary");
                }

                return;
            }

            if (!ServerDisabledCommands.TryAdd(key, commands))
            {
                this._logger.Log($"Failed to add disabled commands {commands} with the key: {key} from the dictionary");
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
                this._logger.Log($"Failed to remove custom disabled commands {removedDisabledCommands} with the key: {key} from the dictionary");
            }
        }


        /// <inheritdoc />
        public async Task LoadAllDisabledCommands()
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var servers = await db.Guilds.Where(w => w.DisabledCommands != null).ToListAsync();
            foreach (var server in servers)
            {
                StoreDisabledCommands(server.DisabledCommands, server.DiscordGuildId);
            }
        }
    }
}
