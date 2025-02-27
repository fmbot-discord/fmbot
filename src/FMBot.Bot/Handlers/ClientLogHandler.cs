using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Shared.Domain.Enums;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class ClientLogHandler
{
    private readonly IMemoryCache _cache;
    private readonly DiscordShardedClient _client;
    private readonly ChannelToggledCommandService _channelToggledCommandService;
    private readonly GuildDisabledCommandService _guildDisabledCommandService;
    private readonly DisabledChannelService _disabledChannelService;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;

    public ClientLogHandler(DiscordShardedClient client,
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
        this._client.Log += LogEvent;
        this._client.ShardLatencyUpdated += ShardLatencyEvent;
        this._client.ShardDisconnected += ShardDisconnectedEvent;
        this._client.ShardConnected += ShardConnectedEvent;
        this._client.JoinedGuild += ClientJoinedGuildEvent;
        this._client.LeftGuild += ClientLeftGuildEvent;
    }

    private Task ClientJoinedGuildEvent(SocketGuild guild)
    {
        _ = Task.Run(() => ClientJoinedGuild(guild));
        return Task.CompletedTask;
    }

    private Task ClientLeftGuildEvent(SocketGuild guild)
    {
        _ = Task.Run(() => ClientLeftGuild(guild));
        return Task.CompletedTask;
    }

    private Task LogEvent(LogMessage logMessage)
    {
        Task.Run(() =>
        {
            switch (logMessage.Severity)
            {
                case LogSeverity.Critical:
                    Log.Fatal(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
                case LogSeverity.Error:
                    Log.Error(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
                case LogSeverity.Warning:
                    Log.Warning(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
                case LogSeverity.Info:
                    Log.Information(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
                case LogSeverity.Verbose:
                    Log.Verbose(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
                case LogSeverity.Debug:
                    Log.Debug(logMessage.Exception, "{logMessageSource} | {logMessage}", logMessage.Source, logMessage.Message);
                    break;
            }

        });
        return Task.CompletedTask;
    }

    private Task ShardDisconnectedEvent(Exception exception, DiscordSocketClient shard)
    {
        _ = Task.Run(() => ShardDisconnected(exception, shard));
        return Task.CompletedTask;
    }

    private Task ShardLatencyEvent(int oldPing, int updatePing, DiscordSocketClient shard)
    {
        _ = Task.Run(() => ShardLatencyUpdated(oldPing, updatePing, shard));
        return Task.CompletedTask;
    }

    private Task ShardConnectedEvent(DiscordSocketClient shard)
    {
        _ = Task.Run(() => ShardConnected(shard));
        return Task.CompletedTask;
    }

    private void ShardDisconnected(Exception exception, DiscordSocketClient shard)
    {
        Statistics.ShardDisConnected.Inc();

        Statistics.ConnectedShards.Set(this._client.Shards.Count(w => w.ConnectionState == ConnectionState.Connected));

        Log.Warning("ShardDisconnected: shard #{shardId} Disconnected", shard.ShardId, exception);
    }

    private void ShardConnected(DiscordSocketClient shard)
    {
        Statistics.ShardConnected.Inc();

        Statistics.ConnectedShards.Set(this._client.Shards.Count(w => w.ConnectionState == ConnectionState.Connected));

        Log.Information("ShardConnected: shard #{shardId} with {shardLatency} ms", shard.ShardId, shard.Latency);
    }

    private void ShardLatencyUpdated(int oldPing, int updatePing, DiscordSocketClient shard)
    {
        Statistics.ConnectedShards.Set(this._client.Shards.Count(w => w.ConnectionState == ConnectionState.Connected));

        if (updatePing < 400 && oldPing < 400) return;

        Log.Information("Shard: #{shardId} Latency update from {oldPing} ms to {updatePing} ms",
            shard.ShardId, oldPing, updatePing);
    }

    private async Task ClientJoinedGuild(SocketGuild guild)
    {
        Log.Information(
            "JoinedGuild: {guildName} / {guildId} | {memberCount} members", guild.Name, guild.Id, guild.MemberCount);

        _ = this._channelToggledCommandService.ReloadToggledCommands(guild.Id);
        _ = this._guildDisabledCommandService.ReloadDisabledCommands(guild.Id);
        _ = this._disabledChannelService.ReloadDisabledChannels(guild.Id);
        _ = this._prefixService.ReloadPrefix(guild.Id);

        _ = IndexServer(guild);
    }

    private async Task IndexServer(SocketGuild guild)
    {
        try
        {
            var users = new List<IGuildUser>();
            await foreach (var awaitedUsers in guild.GetUsersAsync())
            {
                users.AddRange(awaitedUsers);
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

    private async Task ClientLeftGuild(SocketGuild guild)
    {
        var keepData = false;

        var key = $"{guild.Id}-keep-data";
        if (this._cache.TryGetValue(key, out _))
        {
            keepData = true;
        }

        if (BotTypeExtension.GetBotType(this._client.CurrentUser.Id) == BotType.Beta)
        {
            keepData = true;
        }

        if (!keepData)
        {
            Log.Information(
                "LeftGuild: {guildName} / {guildId} | {memberCount} members", guild.Name, guild.Id, guild.MemberCount);

            _ = this._channelToggledCommandService.RemoveToggledCommandsForGuild(guild.Id);
            _ = this._disabledChannelService.RemoveDisabledChannelsForGuild(guild.Id);
            _ = this._guildService.RemoveGuildAsync(guild.Id);
        }
        else
        {
            Log.Information(
                "LeftGuild: {guildName} / {guildId} | {memberCount} members (skipped delete)", guild.Name, guild.Id, guild.MemberCount);
        }
    }
}
