using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Gateway;
using Serilog;
using Shared.Domain.Enums;
using DiscordGuild = NetCord.Gateway.Guild;
using DiscordGuildUser = NetCord.GuildUser;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class ClientLogHandler
{
    private readonly IMemoryCache _cache;
    private readonly ShardedGatewayClient _client;
    private readonly ChannelToggledCommandService _channelToggledCommandService;
    private readonly GuildDisabledCommandService _guildDisabledCommandService;
    private readonly DisabledChannelService _disabledChannelService;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;

    public ClientLogHandler(ShardedGatewayClient client,
        ChannelToggledCommandService channelToggledCommandService,
        GuildDisabledCommandService guildDisabledCommandService,
        GuildService guildService,
        IPrefixService prefixService,
        IndexService indexService,
        IMemoryCache cache,
        DisabledChannelService disabledChannelService)
    {
        this._client = client;
        this._channelToggledCommandService = channelToggledCommandService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._indexService = indexService;
        this._cache = cache;
        this._disabledChannelService = disabledChannelService;

        // Shard connection events
        this._client.Ready += ShardReady;
        this._client.Connect += ShardConnected;
        this._client.Disconnect += ShardDisconnected;
        this._client.Resume += ShardResumed;

        // Guild events
        this._client.GuildCreate += ClientJoinedGuildEvent;
        this._client.GuildDelete += ClientLeftGuildEvent;
    }

    private ValueTask ShardReady(GatewayClient client, ReadyEventArgs args)
    {
        Log.Information("ShardReady: Shard #{shardId} is ready with {guildCount} guilds",
            client.Shard?.Id ?? 0, args.GuildIds.Count);
        return ValueTask.CompletedTask;
    }

    private ValueTask ShardConnected(GatewayClient client)
    {
        Log.Information("ShardConnected: Shard #{shardId} connected",
            client.Shard?.Id ?? 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask ShardDisconnected(GatewayClient client, DisconnectEventArgs args)
    {
        Log.Warning("ShardDisconnected: Shard #{shardId} disconnected",
            client.Shard?.Id ?? 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask ShardResumed(GatewayClient client)
    {
        Log.Information("ShardResumed: Shard #{shardId} resumed session",
            client.Shard?.Id ?? 0);
        return ValueTask.CompletedTask;
    }

    private ValueTask ClientJoinedGuildEvent(GatewayClient client, GuildCreateEventArgs guildCreateEventArgs)
    {
        _ = Task.Run(() => ClientJoinedGuild(guildCreateEventArgs.Guild));
        return ValueTask.CompletedTask;
    }

    private ValueTask ClientLeftGuildEvent(GatewayClient client, GuildDeleteEventArgs args)
    {
        if (!args.IsUnavailable)
        {
            _ = Task.Run(() => ClientLeftGuild(args));
        }
        return ValueTask.CompletedTask;
    }

    private async Task ClientJoinedGuild(DiscordGuild guild)
    {
        Log.Information(
            "JoinedGuild: {guildName} / {guildId} | {memberCount} members", guild.Name, guild.Id, guild.ApproximateUserCount);

        var dbGuild = await this._guildService.GetGuildAsync(guild.Id);
        if (dbGuild?.GuildFlags.HasValue == true && dbGuild.GuildFlags.Value.HasFlag(GuildFlags.Banned))
        {
            Log.Information("JoinedGuild: {guildName} / {guildId} | Guild is banned, leaving immediately", guild.Name, guild.Id);
            await guild.LeaveAsync();
            return;
        }

        _ = this._channelToggledCommandService.ReloadToggledCommands(guild.Id);
        _ = this._guildDisabledCommandService.ReloadDisabledCommands(guild.Id);
        _ = this._disabledChannelService.ReloadDisabledChannels(guild.Id);
        _ = this._prefixService.ReloadPrefix(guild.Id);

        _ = IndexServer(guild);
    }

    private async Task IndexServer(DiscordGuild guild)
    {
        try
        {
            var users = new List<DiscordGuildUser>();
            await foreach (var user in guild.GetUsersAsync())
            {
                users.Add(user);
            }

            Log.Information(
                "JoinedGuild: {guildName} / {guildId} | downloaded {userDownloadedCount} members for indexing", guild.Name, guild.Id, users.Count);

            if (users.Any())
            {
                await this._indexService.StoreGuildUsers(guild, users);
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in JoinedGuild / IndexServer", e);
        }
    }

    private async Task ClientLeftGuild(GuildDeleteEventArgs args)
    {
        var keepData = false;

        var key = $"{args.GuildId}-keep-data";
        if (this._cache.TryGetValue(key, out _))
        {
            keepData = true;
        }

        if (BotTypeExtension.GetBotType(this._client.GetCurrentUser()!.Id) == BotType.Beta)
        {
            keepData = true;
        }

        var dbGuild = await this._guildService.GetGuildAsync(args.GuildId);
        if (dbGuild?.GuildFlags.HasValue == true && dbGuild.GuildFlags.Value.HasFlag(GuildFlags.Banned))
        {
            keepData = true;
        }

        if (!keepData)
        {
            Log.Information(
                "LeftGuild: {guildId}", args.GuildId);

            _ = this._channelToggledCommandService.RemoveToggledCommandsForGuild(args.GuildId);
            _ = this._disabledChannelService.RemoveDisabledChannelsForGuild(args.GuildId);
            _ = this._guildService.RemoveGuildAsync(args.GuildId);
        }
        else
        {
            Log.Information(
                "LeftGuild: {guildId} (skipped delete)", args.GuildId);
        }
    }
}
