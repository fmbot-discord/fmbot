using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Handlers;

public class UserEventHandler
{
    private readonly DiscordShardedClient _client;
    private readonly IIndexService _indexService;
    private readonly CrownService _crownService;
    private readonly BotSettings _botSettings;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;

    public UserEventHandler(DiscordShardedClient client, IIndexService indexService, CrownService crownService, IOptions<BotSettings> botSettings, UserService userService, SupporterService supporterService)
    {
        this._client = client;
        this._indexService = indexService;
        this._crownService = crownService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._client.UserJoined += UserJoined;
        this._client.UserLeft += UserLeft;
        this._client.UserBanned += UserBanned;
        this._client.GuildMemberUpdated += GuildMemberUpdated;
        this._botSettings = botSettings.Value;
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

    private async Task UserJoined(SocketGuildUser socketGuildUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserJoined)).Inc();

        if (socketGuildUser.Guild.Id == this._botSettings.Bot.BaseServerId &&
            this._botSettings.Bot.FeaturedMaster == true)
        {
            var user = await this._userService.GetUserAsync(socketGuildUser.Id);
            if (user is { UserType: UserType.Supporter })
            {
                await this._supporterService.ModifyGuildRole(socketGuildUser.Id);
            }
        }
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
