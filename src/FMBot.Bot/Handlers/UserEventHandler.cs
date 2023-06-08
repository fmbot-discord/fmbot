using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;

namespace FMBot.Bot.Handlers;

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
        this._client.UserLeft += UserLeft;
        this._client.UserBanned += UserBanned;
        this._client.GuildMemberUpdated += GuildMemberUpdated;
    }

    private async Task GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> cacheable, SocketGuildUser newGuildUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(GuildMemberUpdated)).Inc();

        if (!PublicProperties.RegisteredUsers.ContainsKey(cacheable.Id) ||
            !PublicProperties.RegisteredUsers.ContainsKey(newGuildUser.Id))
        {
            return;
        }

        if (PublicProperties.PremiumServers.ContainsKey(newGuildUser.Guild.Id))
        {
            if (cacheable.Value?.DisplayName == newGuildUser.DisplayName &&
                Equals(cacheable.Value?.Roles, newGuildUser.Roles))
            {
                return;
            }
        }
        else
        {
            if (cacheable.Value?.DisplayName == newGuildUser.DisplayName)
            {
                return;
            }
        }

        _ = this._indexService.AddOrUpdateGuildUser(newGuildUser);
    }

    private async Task UserLeft(SocketGuild socketGuild, SocketUser socketUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserLeft)).Inc();

        if (socketGuild != null && socketUser != null)
        {
            _ = this._indexService.RemoveUserFromGuild(socketUser.Id, socketGuild.Id);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(socketUser.Id, socketGuild.Id);
        }
    }

    private async Task UserBanned(SocketUser guildUser, SocketGuild guild)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserBanned)).Inc();

        _ = this._indexService.RemoveUserFromGuild(guildUser.Id, guild.Id);
        _ = this._crownService.RemoveAllCrownsFromDiscordUser(guildUser.Id, guild.Id);
    }
}
