using System;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Serilog;
using DiscordGuildUser = NetCord.GuildUser;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class UserEventHandler
{
    private readonly ShardedGatewayClient _client;
    private readonly IndexService _indexService;
    private readonly CrownService _crownService;
    private readonly BotSettings _botSettings;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;

    public UserEventHandler(ShardedGatewayClient client, IndexService indexService, CrownService crownService, IOptions<BotSettings> botSettings, UserService userService, SupporterService supporterService)
    {
        this._client = client;
        this._indexService = indexService;
        this._crownService = crownService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._client.GuildUserAdd += UserJoined;
        this._client.GuildUserRemove += UserLeft;
        this._client.GuildBanAdd += UserBanned;
        this._client.GuildUserUpdate += GuildMemberUpdated;
        this._client.EntitlementCreate += EntitlementCreated;
        this._client.EntitlementUpdate += EntitlementUpdated;
        this._botSettings = botSettings.Value;
    }

    private ValueTask GuildMemberUpdated(GatewayClient client, DiscordGuildUser newGuildUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(GuildMemberUpdated)).Inc();

        if (newGuildUser.Id == Constants.BotProductionId || newGuildUser.Id == Constants.BotBetaId)
        {
            return ValueTask.CompletedTask;
        }

        if (!PublicProperties.RegisteredUsers.ContainsKey(newGuildUser.Id))
        {
            return ValueTask.CompletedTask;
        }

        _ = this._indexService.AddOrUpdateGuildUser(newGuildUser);
        return ValueTask.CompletedTask;
    }

    private async ValueTask UserJoined(GatewayClient client, DiscordGuildUser socketGuildUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserJoined)).Inc();

        if (socketGuildUser.GuildId == this._botSettings.Bot.BaseServerId &&
            client.Id == Constants.BotProductionId)
        {
            var user = await this._userService.GetUserAsync(socketGuildUser.Id);
            if (user is { UserType: UserType.Supporter })
            {
                await this._supporterService.ModifyGuildRole(socketGuildUser.Id);
            }

            if (user == null)
            {
                var webhookClient = Services.Guild.WebhookService.CreateWebhookClientFromUrl(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

                var embed = new EmbedProperties();

                embed.WithTitle("User without .fmbot account joined");
                embed.WithDescription($"<@{socketGuildUser.Id}> - `{socketGuildUser.Username}` - **{socketGuildUser.GetDisplayName()}**");
                embed.WithTimestamp(DateTimeOffset.UtcNow);
                embed.WithFooter($"{socketGuildUser.Id}");

                await webhookClient.ExecuteAsync(new WebhookMessageProperties
                {
                    Embeds = [embed]
                });
            }
        }
    }

    private async ValueTask EntitlementCreated(GatewayClient client, Entitlement entitlement)
    {
        Statistics.DiscordEvents.WithLabels(nameof(EntitlementCreated)).Inc();

        if (entitlement.UserId.HasValue)
        {
            Log.Information("EntitlementCreated - {userId} - received event", entitlement.UserId.Value);

            await this._supporterService.UpdateSingleDiscordSupporter(entitlement.UserId.Value);
        }
    }

    private async ValueTask EntitlementUpdated(GatewayClient client, Entitlement entitlement)
    {
        Statistics.DiscordEvents.WithLabels(nameof(EntitlementUpdated)).Inc();

        if (entitlement.UserId.HasValue)
        {
            Log.Information("EntitlementUpdated - {userId} - received event", entitlement.UserId.Value);

            await this._supporterService.UpdateSingleDiscordSupporter(entitlement.UserId.Value);
        }
    }

    private async ValueTask UserLeft(GatewayClient client, GuildUserRemoveEventArgs args)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserLeft)).Inc();

        if (PublicProperties.RegisteredUsers.ContainsKey(args.User.Id))
        {
            _ = this._indexService.RemoveUserFromGuild(args.User.Id, args.GuildId);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(args.User.Id, args.GuildId);
        }
    }

    private async ValueTask UserBanned(GatewayClient client, GuildBanEventArgs args)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserBanned)).Inc();

        if (PublicProperties.RegisteredUsers.ContainsKey(args.User.Id))
        {
            _ = this._indexService.RemoveUserFromGuild(args.User.Id, args.GuildId);
            _ = this._crownService.RemoveAllCrownsFromDiscordUser(args.User.Id, args.GuildId);
        }
    }
}
