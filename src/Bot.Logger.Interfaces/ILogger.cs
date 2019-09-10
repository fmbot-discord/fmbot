using System;

namespace Bot.Logger.Interfaces
{
    public interface ILogger
    {

        /// <summary>
        /// Logs a message to the console and logs the message to the Misc logs folder.
        /// The <paramref name="color"/> is by default <see cref="ConsoleColor.Gray"/>
        /// </summary>
        /// <param name="message">The message that the user typed.</param>
        /// <param name="color">The color that the message will have when printing something to the console.</param>
        void Log(string message, ConsoleColor color = ConsoleColor.Gray);


        /// <summary>
        /// Logs a command error to text file.
        /// </summary>
        /// <param name="folder">The folder where you want to store the message.</param>
        /// <param name="errorReason">The error reason.</param>
        /// <param name="message">The message that the user typed.</param>
        /// <param name="username">The username of the user.</param>
        /// <param name="guildName">The name of the server.</param>
        /// <param name="guildId">The id of the server.</param>
        void LogError(string folder, string errorReason, string message = null, string username = null, string guildName = null, ulong? guildId = null);


        /// <summary>
        /// Logs a message to text file.
        /// </summary>
        /// <param name="folder">The folder where you want to store the message.</param>
        /// <param name="text">The text you want to save.</param>
        void Log(string folder, string text);


        /// <summary>
        /// Logs a command error to text file.
        /// </summary>
        /// <param name="id">The id of the server.</param>
        /// <param name="shardId">The id of the shard.</param>
        /// <param name="channelId">The channel id where the command was used.</param>
        /// <param name="userId">The user id of the user.</param>
        /// <param name="commandName">The name of the command that was used.</param>
        void LogCommandUsed(ulong? id, int shardId, ulong channelId, ulong userId, string commandName);
    }
}
