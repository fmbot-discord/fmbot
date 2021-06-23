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
            this._client.GuildMemberUpdated += GuildUserUpdated;
        }

        private async Task UserLeftGuild(SocketGuildUser guildUser)
        {
            _ = this._indexService.RemoveUserFromGuild(guildUser);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(guildUser);
        }

        private async Task GuildUserUpdated(SocketGuildUser oldGuildUser, SocketGuildUser newGuildUser)
        {
            _ = this._indexService.UpdateDiscordUser(newGuildUser);
        }
    }
}
