using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Services
{
    public class PrefixService : IPrefixService
    {
        private readonly Logger.Logger _logger;

        /// <summary>
        ///     The <see cref="ConcurrentDictionary{TKey,TValue}" /> that contains all the custom prefixes.
        /// </summary>
        private static readonly ConcurrentDictionary<ulong, string> ServerPrefixes =
            new ConcurrentDictionary<ulong, string>();

        public PrefixService(Logger.Logger logger)
        {
            this._logger = logger;
        }

        /// <inheritdoc />
        public void StorePrefix(string prefix, ulong key)
        {
            if (ServerPrefixes.ContainsKey(key))
            {
                var oldPrefix = GetPrefix(key);
                if (!ServerPrefixes.TryUpdate(key, prefix, oldPrefix))
                {
                    this._logger.Log($"Failed to update custom prefix {prefix} with the key: {key} from the dictionary");
                }

                return;
            }

            if (!ServerPrefixes.TryAdd(key, prefix))
            {
                this._logger.Log($"Failed to add custom prefix {prefix} with the key: {key} from the dictionary");
            }
        }


        /// <inheritdoc />
        public string GetPrefix(ulong? key)
        {
            if (!key.HasValue)
            {
                return null;
            }

            return !ServerPrefixes.ContainsKey(key.Value) ? null : ServerPrefixes[key.Value];
        }


        /// <inheritdoc />
        public void RemovePrefix(ulong key)
        {
            if (!ServerPrefixes.ContainsKey(key))
            {
                return;
            }

            if (!ServerPrefixes.TryRemove(key, out var removedPrefix))
            {
                this._logger.Log($"Failed to remove custom prefix {removedPrefix} with the key: {key} from the dictionary");
            }
        }


        /// <inheritdoc />
        public async Task LoadAllPrefixes()
        {
            using var db = new FMBotDbContext();
            var servers = await db.Guilds.Where(w => w.Prefix != null).ToListAsync();
            foreach (var server in servers)
            {
                StorePrefix(server.Prefix, server.DiscordGuildId);
            }
        }
    }
}
