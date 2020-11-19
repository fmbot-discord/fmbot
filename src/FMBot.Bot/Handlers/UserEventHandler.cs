using System.Threading.Tasks;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.WhoKnows;

namespace FMBot.Bot.Handlers
{
    public class UserEventHandler
    {
        private readonly DiscordShardedClient _client;
        private readonly IIndexService _indexService;
        private readonly CrownService _crownService;

        public UserEventHandler(DiscordShardedClient client, IIndexService indexService, CrownService crownService)
        {
            this._client = client;
            this._indexService = indexService;
            this._crownService = crownService;
            this._client.UserLeft += UserLeftGuild;
        }

        private async Task UserLeftGuild(SocketGuildUser guildUser)
        {
            await this._indexService.RemoveUserFromGuild(guildUser);
            await this._crownService.RemoveAllCrownsFromDiscordUser(guildUser);
        }
    }
}
