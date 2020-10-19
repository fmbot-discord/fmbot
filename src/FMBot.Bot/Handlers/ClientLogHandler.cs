using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Serilog;

namespace FMBot.Bot.Handlers
{
    public class ClientLogHandler
    {
        private readonly DiscordShardedClient _client;

        public ClientLogHandler(DiscordShardedClient client)
        {
            this._client = client;
        }

        public void Initialize()
        {
            this._client.Log += LogEvent;
            this._client.ShardLatencyUpdated += ShardLatencyEvent;
            this._client.ShardDisconnected += ShardDisconnectedEvent;
            this._client.ShardConnected += ShardConnectedEvent;
            this._client.JoinedGuild += ClientJoinedGuildEvent;
            this._client.LeftGuild += ClientLeftGuild;
        }

        private Task ClientJoinedGuildEvent(SocketGuild guild)
        {
            Task.Run(async () => ClientJoinedGuild(guild));
            return Task.CompletedTask;
        }

        private Task LogEvent(LogMessage logMessage)
        {
            Task.Run(() =>
            {
                // If log message is a Serializer Error, Log the message to the SerializerError folder.
                if (logMessage.Message.Contains("Serializer Error"))
                    Log.Logger.Error(
                        $"Source: {logMessage.Source} Exception: {logMessage.Exception} Message: {logMessage.Message}");
            });
            return Task.CompletedTask;
        }

        private Task ShardDisconnectedEvent(Exception exception, DiscordSocketClient shard)
        {
            Task.Run(async () => ShardDisconnected(exception, shard));
            return Task.CompletedTask;
        }

        private Task ShardLatencyEvent(int oldPing, int updatePing, DiscordSocketClient shard)
        {
            Task.Run(async () => ShardLatencyUpdated(oldPing, updatePing, shard));
            return Task.CompletedTask;
        }

        private Task ShardConnectedEvent(DiscordSocketClient shard)
        {
            Task.Run(async () => ShardConnected(shard));
            return Task.CompletedTask;
        }

        private void ShardDisconnected(Exception exception, DiscordSocketClient shard)
        {
            Log.Warning($"Shard disconnected: `{shard.ShardId}` Disconnected with the reason {exception.Message}");
        }

        private void ShardConnected(DiscordSocketClient shard)
        {
            Log.Logger.Information($"Shard connected: {shard.ShardId} with **{shard.Latency}** ms");
        }

        private void ShardLatencyUpdated(int oldPing, int updatePing, DiscordSocketClient shard)
        {
            // If new or old latency if lager then 500ms.
            if (updatePing < 500 && oldPing < 500) return;
            Log.Logger.Information($"Shard: `{shard.ShardId}` Latency update from **{oldPing}** ms to **{updatePing}** ms");
        }

        private void ClientJoinedGuild(SocketGuild guild)
        {
            Log.Logger.Information($"Joined server: {guild.Name}, Id: {guild.Id}, MemberCount: {guild.MemberCount}.");
        }

        private async Task ClientLeftGuild(SocketGuild guild)
        {
            Log.Logger.Information($"Left server: {guild.Name}, Id: {guild.Id}.");
        }
    }
}
