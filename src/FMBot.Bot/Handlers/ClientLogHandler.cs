using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Configurations;

namespace FMBot.Bot.Handlers
{
    public class ClientLogHandler
    {
        private readonly DiscordShardedClient _client;
        private readonly Logger.Logger _logger;

        /// <summary>
        /// Creates a new <see cref="ClientLogHandler"/>.
        /// </summary>
        /// <param name="client">The <see cref="DiscordShardedClient"/> that will be used.</param>
        /// <param name="logger">The <see cref="ILogger"/> that will be used to log all the messages.</param>
        public ClientLogHandler(DiscordShardedClient client, Logger.Logger logger)
        {
            _client = client;
            _logger = logger;
        }


        /// <inheritdoc />
        public void Initialize()
        {
            _client.Log += LogEvent;
            _client.ShardLatencyUpdated += ShardLatencyEvent;
            _client.ShardDisconnected += ShardDisconnectedEvent;
            _client.ShardConnected += ShardConnectedEvent;
            _client.JoinedGuild += ClientJoinedGuildEvent;
            _client.LeftGuild += ClientLeftGuild;

        }

        /// <summary>
        /// Handles the <see cref="BaseSocketClient.JoinedGuild"/> event.
        /// </summary>
        /// <param name="guild">The server that the client joined.</param>
        private Task ClientJoinedGuildEvent(SocketGuild guild)
        {
            Task.Run(async () => await ClientJoinedGuild(guild));
            return Task.CompletedTask;
        }


        /// <summary>
        /// Handles the <see cref="DiscordShardedClient.Log"/> event.
        /// </summary>
        /// <param name="logMessage">The log message.</param>
        private Task LogEvent(LogMessage logMessage)
        {
            Task.Run(() =>
            {
                // If log message is a Serializer Error, Log the message to the SerializerError folder.
                if (logMessage.Message.Contains("Serializer Error")) _logger.LogError($"Source: {logMessage.Source} Exception: {logMessage.Exception} Message: {logMessage.Message}");
                Log(logMessage.Message);
            });
            return Task.CompletedTask;
        }


        /// <summary>
        /// Logs a string to the console.
        /// </summary>
        /// <param name="message">The message that will be logged.</param>
        private void Log(string message)
        {
            if (message.Contains("Unknown User") || message.Contains("Unknown Guild"))
            {
                _logger.Log(message);
                return;
            }

            _logger.Log(message);
        }


        /// <summary>
        /// Handles the <see cref="DiscordShardedClient.ShardDisconnected"/> event.
        /// </summary>
        /// <param name="exception">The exception of the disconnected shard.</param>
        /// <param name="shard">The shard that got disconnected.</param>
        private Task ShardDisconnectedEvent(Exception exception, DiscordSocketClient shard)
        {
            Task.Run(async () => await ShardDisconnectedAsync(exception, shard));
            return Task.CompletedTask;
        }


        /// <summary>
        /// Handles the <see cref="DiscordShardedClient.ShardLatencyUpdated"/> event.
        /// </summary>
        /// <param name="oldPing">The latency value before it was updated.</param>
        /// <param name="updatePing">The new latency value</param>
        /// <param name="shard">The shard that got disconnected.</param>
        private Task ShardLatencyEvent(int oldPing, int updatePing, DiscordSocketClient shard)
        {
            Task.Run(async () => await ShardLatencyUpdatedAsync(oldPing, updatePing, shard));
            return Task.CompletedTask;
        }


        /// <summary>
        /// Handles the <see cref="DiscordShardedClient.ShardConnected"/> event.
        /// </summary>
        /// <param name="shard">The shard that got connected.</param>
        private Task ShardConnectedEvent(DiscordSocketClient shard)
        {
            Task.Run(async () => await ShardConnectedAsync(shard));
            return Task.CompletedTask;
        }


        /// <summary>
        /// Sends a message that a shard disconnected
        /// </summary>
        /// <param name="exception">The exception of the disconnected shard.</param>
        /// <param name="shard">The shard that got disconnected.</param>
        private async Task ShardDisconnectedAsync(Exception exception, DiscordSocketClient shard)
        {
            try
            {
                var channel = _client.GetGuild(Convert.ToUInt64(ConfigData.Data.BaseServer)).GetTextChannel(Convert.ToUInt64(ConfigData.Data.ExceptionChannel));
                await channel.SendMessageAsync($"<:RedStatus:519932993343586350> Shard: `{shard.ShardId}` Disconnected with the reason {exception.Message}");
            }
            catch (Exception)
            {
                // Logs the message to a txt file if it was unable to send the message.
                _logger.LogException($"Shard: {shard.ShardId}", exception);
            }
        }


        /// <summary>
        /// Sends a message that a shard connected.
        /// </summary>
        /// <param name="shard">The shard that got connected.</param>
        private async Task ShardConnectedAsync(DiscordSocketClient shard)
        {
            try
            {
                await Task.Delay(30 * 1000);
                var channel = _client.GetGuild(Convert.ToUInt64(ConfigData.Data.BaseServer)).GetTextChannel(Convert.ToUInt64(ConfigData.Data.ExceptionChannel));
                await channel.SendMessageAsync($"<:GreenStatus:519932750296514605> Shard: `{shard.ShardId}` Connected with {shard.Latency}ms");
            }
            catch (Exception)
            {
                // Logs the message to a txt file if it was unable to send the message.
                _logger.Log($"Shard: {shard.ShardId} with **{shard.Latency}** ms");
            }
        }


        /// <summary>
        /// Sends a message that a shard latency was updated.
        /// </summary>
        /// <param name="oldPing">The latency value before it was updated.</param>
        /// <param name="updatePing">The new latency value.</param>
        /// <param name="shard">The shard that got disconnected.</param>
        private async Task ShardLatencyUpdatedAsync(int oldPing, int updatePing, DiscordSocketClient shard)
        {
            // If new or old latency if lager then 500ms.
            if (updatePing < 500 && oldPing < 500) return;
            try
            {
                var channel = _client.GetGuild(Convert.ToUInt64(ConfigData.Data.BaseServer)).GetTextChannel(Convert.ToUInt64(ConfigData.Data.ExceptionChannel));
                await channel.SendMessageAsync($"Shard: `{shard.ShardId}` Latency update from **{oldPing}** ms to **{updatePing}** ms");
            }
            catch (Exception e)
            {
                // Logs the message to a txt file if it was unable to send the message.
                _logger.LogException($"Shard: {shard.ShardId}", e);
            }
        }


        /// <summary>
        /// Sends a message that the client joined a server.
        /// </summary>
        /// <param name="guild">The server that the client joined.</param>
        private async Task ClientJoinedGuild(SocketGuild guild)
        {
            try
            {
                var channel = _client.GetGuild(Convert.ToUInt64(ConfigData.Data.BaseServer)).GetTextChannel(Convert.ToUInt64(ConfigData.Data.ExceptionChannel));
                await channel.SendMessageAsync($"Joined server: {guild.Name}, Id: {guild.Id}, MemberCount: {guild.MemberCount}.");
            }
            catch (Exception e)
            {
                // Logs the message to a txt file if it was unable to send the message.
                _logger.LogException($"Joined server: {guild.Name}, Id: {guild.Id}, MemberCount: {guild.MemberCount}.", e);
            }
        }

        /// <summary>
        /// Sends a message that the client left a server.
        /// </summary>
        /// <param name="guild">The server that the client left.</param>
        private async Task ClientLeftGuild(SocketGuild guild)
        {
            try
            {
                var channel = _client.GetGuild(Convert.ToUInt64(ConfigData.Data.BaseServer)).GetTextChannel(Convert.ToUInt64(ConfigData.Data.ExceptionChannel));
                await channel.SendMessageAsync($"Left server: {guild.Name}, Id: {guild.Id}.");
            }
            catch (Exception e)
            {
                // Logs the message to a txt file if it was unable to send the message.
                _logger.LogException($"Left guild.", e);
            }
        }
    }
}
