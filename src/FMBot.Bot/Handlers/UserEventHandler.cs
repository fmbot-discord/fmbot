using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;

namespace FMBot.Bot.Handlers
{
    public class UserEventHandler
    {
        private readonly DiscordShardedClient _client;
        private readonly IIndexService _indexService;

        public UserEventHandler(DiscordShardedClient client, IIndexService indexService)
        {
            this._client = client;
            this._indexService = indexService;
            this._client.UserLeft += UserLeftGuild;
        }

        private async Task UserLeftGuild(SocketGuildUser guildUser)
        {
            await this._indexService.RemoveUserFromGuild(guildUser);
        }
    }
}
