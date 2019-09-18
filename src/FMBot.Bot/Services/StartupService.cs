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
using Microsoft.Extensions.Configuration;

namespace FMBot.Bot.Services
{
    public class StartupService
    {
        private readonly IServiceProvider _provider;
        private readonly DiscordShardedClient _discord;
        private readonly CommandService _commands;
        private readonly LastFMService _lastFmService;

        public StartupService(
            IServiceProvider provider,
            DiscordShardedClient discord,
            CommandService commands)
        {
            _provider = provider;
            _discord = discord;
            _commands = commands;
        }

        public async Task StartAsync()
        {
            Console.WriteLine("Starting bot...");

            string discordToken = ConfigData.Data.Token;     // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
                throw new Exception("Please enter your bots token into the `/Configs/ConfigData.json` file.");

            await _lastFmService;

            await _discord.LoginAsync(TokenType.Bot, discordToken);     // Login to discord
            await _discord.StartAsync();                                // Connect to the websocket

            await _discord.SetGameAsync("🎶 Say " + ConfigData.Data.CommandPrefix + "fmhelp to use 🎶").ConfigureAwait(false);
            await _discord.SetStatusAsync(UserStatus.DoNotDisturb);

            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _provider);     // Load commands and modules into the command service
        }
    }
}
