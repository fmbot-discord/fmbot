using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.WhoKnows;
using Serilog;

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
            this._client.UserBanned += UserBanned;
            //this._client.GuildMemberUpdated += GuildUserUpdated;
        }

        private async Task GuildUserUpdated(SocketGuildUser oldGuildUser, SocketGuildUser newGuildUser)
        {
            Log.Information($"GuildUserUpdated {oldGuildUser.Nickname} - {newGuildUser.Nickname}");
            _ = this._indexService.UpdateGuildUserEvent(newGuildUser);
        }
        
        private async Task UserLeftGuild(SocketGuildUser guildUser)
        {
            _ = this._indexService.RemoveUserFromGuild(guildUser.Id, guildUser.Guild.Id);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(guildUser.Id, guildUser.Guild.Id);
        }

        private async Task UserBanned(SocketUser guildUser, SocketGuild guild)
        {
            _ = this._indexService.RemoveUserFromGuild(guildUser.Id, guild.Id);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(guildUser.Id, guild.Id);
        }
    }
}
