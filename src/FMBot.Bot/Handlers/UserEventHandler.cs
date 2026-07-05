using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
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
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;

    private static readonly ConcurrentDictionary<ulong, string> BotGuildNicknames = new();

    public UserEventHandler(ShardedGatewayClient client, IndexService indexService, CrownService crownService, IOptions<BotSettings> botSettings, UserService userService, SupporterService supporterService, CensorService censorService, GuildService guildService)
    {
        this._client = client;
        this._indexService = indexService;
        this._crownService = crownService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._censorService = censorService;
        this._guildService = guildService;
        this._client.GuildUserAdd += UserJoined;
        this._client.GuildUserRemove += UserLeft;
        this._client.GuildBanAdd += UserBanned;
        this._client.GuildUserUpdate += GuildMemberUpdated;
        this._client.EntitlementCreate += EntitlementCreated;
        this._client.EntitlementUpdate += EntitlementUpdated;
        this._client.EntitlementDelete += EntitlementDeleted;
        this._botSettings = botSettings.Value;
    }

    private async ValueTask GuildMemberUpdated(GatewayClient client, DiscordGuildUser newGuildUser)
    {
        Statistics.DiscordEvents.WithLabels(nameof(GuildMemberUpdated)).Inc();

        if (newGuildUser.Id == Constants.BotProductionId || newGuildUser.Id == Constants.BotBetaId)
        {
            await HandleBotMemberUpdated(client, newGuildUser);
            return;
        }

        if (!PublicProperties.RegisteredUsers.ContainsKey(newGuildUser.Id))
        {
            return;
        }

        _ = this._indexService.AddOrUpdateGuildUser(newGuildUser);
    }

    private async ValueTask HandleBotMemberUpdated(GatewayClient client, DiscordGuildUser botGuildUser)
    {
        var guildId = botGuildUser.GuildId;

        if (!PublicProperties.PremiumServers.ContainsKey(guildId))
        {
            return;
        }

        var nickname = botGuildUser.Nickname;

        if (BotGuildNicknames.TryGetValue(guildId, out var previousNickname) &&
            string.Equals(previousNickname, nickname, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            BotGuildNicknames[guildId] = nickname;
            return;
        }

        bool offensive;
        try
        {
            offensive = await this._censorService.ContainsBadWords(nickname);
        }
        catch (Exception e)
        {
            Log.Error(e, "BotBranding: Failed to check custom bot nickname for guild {guildId}", guildId);
            return;
        }

        var guild = await this._guildService.GetGuildAsync(guildId);
        var guildName = guild?.Name != null ? StringExtensions.Sanitize(guild.Name) : guildId.ToString();

        if (offensive)
        {
            try
            {
                await client.Rest.ModifyCurrentGuildUserAsync(guildId, o => o.Nickname = "");
            }
            catch (Exception e)
            {
                Log.Error(e, "BotBranding: Failed to reset offensive bot nickname in guild {guildId}", guildId);
                return;
            }

            BotGuildNicknames[guildId] = null;

            Log.Information("BotBranding: Blocked offensive custom bot nickname in guild {guildId} - {nickname}",
                guildId, nickname);

            await this._guildService.SendBotBrandingAuditLog(
                $"🚫 **Blocked offensive custom bot nickname**\n" +
                $"Server: **{guildName}** — `{guildId}`\n" +
                $"Attempted name: `{StringExtensions.Sanitize(nickname)}`",
                warning: true);

            return;
        }

        BotGuildNicknames[guildId] = nickname;

        await this._guildService.SendBotBrandingAuditLog(
            $"✏️ **Custom bot nickname set**\n" +
            $"Server: **{guildName}** — `{guildId}`\n" +
            $"Name: **{StringExtensions.Sanitize(nickname)}**");
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

        if (entitlement.GuildId.HasValue)
        {
            Log.Information("EntitlementCreated - guild {guildId} - received event", entitlement.GuildId.Value);

            await ProcessGuildEntitlement(entitlement.GuildId.Value);
            return;
        }

        if (entitlement.UserId.HasValue)
        {
            Log.Information("EntitlementCreated - {userId} - received event", entitlement.UserId.Value);

            await this._supporterService.UpdateSingleDiscordSupporter(entitlement.UserId.Value);
        }
    }

    private async ValueTask EntitlementUpdated(GatewayClient client, Entitlement entitlement)
    {
        Statistics.DiscordEvents.WithLabels(nameof(EntitlementUpdated)).Inc();

        if (entitlement.GuildId.HasValue)
        {
            Log.Information("EntitlementUpdated - guild {guildId} - received event", entitlement.GuildId.Value);

            await ProcessGuildEntitlement(entitlement.GuildId.Value);
            return;
        }

        if (entitlement.UserId.HasValue)
        {
            Log.Information("EntitlementUpdated - {userId} - received event", entitlement.UserId.Value);

            await this._supporterService.UpdateSingleDiscordSupporter(entitlement.UserId.Value);
        }
    }

    private async ValueTask EntitlementDeleted(GatewayClient client, Entitlement entitlement)
    {
        Statistics.DiscordEvents.WithLabels(nameof(EntitlementDeleted)).Inc();

        if (entitlement.GuildId.HasValue)
        {
            Log.Information("EntitlementDeleted - guild {guildId} - received event", entitlement.GuildId.Value);

            await ProcessGuildEntitlement(entitlement.GuildId.Value);
        }
    }

    private async Task ProcessGuildEntitlement(ulong discordGuildId)
    {
        await this._supporterService.UpdateSingleDiscordPremiumGuild(discordGuildId);
        await this._guildService.RefreshPremiumGuilds();
        await this._supporterService.SendPremiumGuildWelcomeMessages();
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
