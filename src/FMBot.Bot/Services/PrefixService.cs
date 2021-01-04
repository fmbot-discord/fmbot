using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services
{
    public class PrefixService : IPrefixService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private static readonly ConcurrentDictionary<ulong, string> ServerPrefixes =
            new ConcurrentDictionary<ulong, string>();

        public PrefixService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
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
                return null;
            }

            return !ServerPrefixes.ContainsKey(key.Value) ? null : ServerPrefixes[key.Value];
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
    }
}
