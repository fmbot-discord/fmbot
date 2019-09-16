using System;

namespace Bot.Logger.Interfaces
{
    public interface ILogger
    {
        /// <summary>
        /// Logs a message to text file.
        /// </summary>
        /// <param name="text">The text you want to save.</param>
        void Log(string text, ConsoleColor color = ConsoleColor.Gray);


        /// <summary>
        /// Logs a command error to text file.
        /// </summary>
        /// <param name="id">The id of the server.</param>
        /// <param name="shardId">The id of the shard.</param>
        /// <param name="channelId">The channel id where the command was used.</param>
        /// <param name="userId">The user id of the user.</param>
        /// <param name="commandName">The name of the command that was used.</param>
        void LogCommandUsed(ulong? id, ulong channelId, ulong userId, string commandName);

        /// <summary>
        /// Logs a command error to text file.
        /// </summary>
        /// <param name="errorReason">The error reason.</param>
        /// <param name="message">The message that the user typed.</param>
        /// <param name="username">The username of the user.</param>
        /// <param name="guildName">The name of the server.</param>
        /// <param name="guildId">The id of the server.</param>
        void LogError(string errorReason, string message = null, string username = null, string guildName = null, ulong? guildId = null);

        /// <summary>
        /// Logs a command error to text file.
        /// </summary>
        /// <param name="errorReason">The error reason.</param>
        /// <param name="exception">The message that the user typed.</param>
        void LogException(string errorReason, Exception exception);


    }
}
