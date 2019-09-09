using System.Threading.Tasks;

namespace Bot.BotLists.Interfaces.Services
{
    public interface IBotListUpdater
    {


        /// <summary>
        /// Updates the stats for all the bot lists.
        /// </summary>
        /// <param name="botId">The Id of the client.</param>
        /// <param name="shardCount">The total amount of shards the client has.</param>
        /// <param name="guildCounts">The amount of server the client is in.</param>
        /// <param name="shardIds">The shard id.</param>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        Task UpdateBotListStatsAsync(ulong botId, int shardCount, int[] guildCounts, int[] shardIds);

    }
}
