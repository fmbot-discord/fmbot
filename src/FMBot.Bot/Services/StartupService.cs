using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using IF.Lastfm.Core.Api;
using Prometheus;

namespace FMBot.Bot.Services
{
    public class StartupService
    {
        private readonly CommandService _commands;
        private readonly DiscordShardedClient _client;
        private readonly IPrefixService _prefixService;
        private readonly IServiceProvider _provider;
        private readonly Logger.Logger _logger;

        public StartupService(
            IServiceProvider provider,
            DiscordShardedClient discord,
            CommandService commands,
            Logger.Logger logger,
            IPrefixService prefixService)
        {
            this._provider = provider;
            this._client = discord;
            this._commands = commands;
            this._logger = logger;
            this._prefixService = prefixService;
        }

        public async Task StartAsync()
        {
            this._logger.Log("Starting bot");

            var discordToken = ConfigData.Data.Token; // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
            {
                throw new Exception("Please enter your bots token into the `/Configs/ConfigData.json` file.");
            }

            await TestLastFmApi();

            this._logger.Log("Loading all prefixes");
            await this._prefixService.LoadAllPrefixes();

            this._logger.Log("Logging into Discord");
            await this._client.LoginAsync(TokenType.Bot, discordToken);

            this._logger.Log("Starting connection between Discord and the client");
            await this._client.StartAsync();

            await this._client.SetStatusAsync(UserStatus.DoNotDisturb);

            await this._commands
                .AddModulesAsync(
                    Assembly.GetEntryAssembly(),
                    this._provider); // Load commands and modules into the command service

            PrepareCacheFolder();

            await StartMetricsServer();
        }



        private async Task TestLastFmApi()
        {
            var fmClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

            this._logger.Log("Testing Last.FM API");
            var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport");

            if (lastFMUser.Status.ToString().Equals("BadApiKey"))
            {
                this._logger.LogError("BadLastfmApiKey",
                    "Warning! Invalid API key for Last.FM! Please set the proper API keys in the `/Configs/ConfigData.json`! \n \n" +
                    "Exiting in 5 seconds...");

                Thread.Sleep(5000);
                Environment.Exit(0);
            }
            else
            {
                this._logger.Log("Last.FM API test successful");
            }
        }


        private Task StartMetricsServer()
        {
            Thread.Sleep(TimeSpan.FromSeconds(Constants.BotWarmupTimeInSeconds));
            if (this._client == null || this._client.CurrentUser == null)
            {
                this._logger.Log("Delaying metric server startup");
                Thread.Sleep(TimeSpan.FromSeconds(Constants.BotWarmupTimeInSeconds));
            }
            this._logger.Log("Starting metrics server");

            var prometheusPort = 4444;
            if (!this._client.CurrentUser.Id.Equals(Constants.BotProductionId))
            {
                this._logger.Log("Prometheus port selected is non-production");
                prometheusPort = 4422;
            }

            this._logger.Log($"Prometheus starting on port {prometheusPort}");

            var server = new MetricServer("localhost", prometheusPort);
            server.Start();

            this._logger.Log($"Prometheus running on localhost:{prometheusPort}/metrics");
            return Task.CompletedTask;
        }

        private static void PrepareCacheFolder()
        {
            if (!Directory.Exists(FMBotUtil.GlobalVars.CacheFolder))
            {
                Directory.CreateDirectory(FMBotUtil.GlobalVars.CacheFolder);
            }
        }
    }
}
