using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using IF.Lastfm.Core.Api;

namespace FMBot.Bot.Services
{
    public class StartupService
    {
        private readonly IServiceProvider _provider;
        private readonly DiscordShardedClient _discord;
        private readonly CommandService _commands;

        public StartupService(
            IServiceProvider provider,
            DiscordShardedClient discord,
            CommandService commands)
        {
            _provider = provider;
            _discord = discord;
            _commands = commands;
        }

        public async Task StartAsync(Logger.Logger logger)
        {
            logger.Log("Starting bot...");

            string discordToken = ConfigData.Data.Token;     // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
                throw new Exception("Please enter your bots token into the `/Configs/ConfigData.json` file.");

            await TestLastFMAPI(logger);

            await _discord.LoginAsync(TokenType.Bot, discordToken);     // Login to discord
            await _discord.StartAsync();                                // Connect to the websocket

            await _discord.SetGameAsync("Starting bot...");
            await _discord.SetStatusAsync(UserStatus.DoNotDisturb);

            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _provider);     // Load commands and modules into the command service
        }

        private async Task TestLastFMAPI(Logger.Logger logger)
        {
            LastfmClient fmClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

            logger.Log("Checking Last.FM API...");
            var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport");

            if (lastFMUser.Status.ToString().Equals("BadApiKey"))
            {
                logger.LogError("BadLastfmApiKey", "Warning! Invalid API key for Last.FM! Please set the proper API keys in the `/Configs/ConfigData.json`! \n \n" +
                           "Exiting in 5 seconds...");

                Thread.Sleep(5000);
                Environment.Exit(0);
            }
            else
            {
                logger.Log("Last.FM API test successful.");
            }
        }
    }
}
