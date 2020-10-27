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
using FMBot.Domain;
using IF.Lastfm.Core.Api;
using Prometheus;
using Serilog;

namespace FMBot.Bot.Services
{
    public class StartupService
    {
        private readonly CommandService _commands;
        private readonly IDisabledCommandService _disabledCommands;
        private readonly DiscordShardedClient _client;
        private readonly IPrefixService _prefixService;
        private readonly IServiceProvider _provider;
        private readonly Logger.Logger _logger;

        private readonly ILogger logger = Log.ForContext<StartupService>();


        public StartupService(
            IServiceProvider provider,
            DiscordShardedClient discord,
            CommandService commands,
            Logger.Logger logger,
            IPrefixService prefixService,
            IDisabledCommandService disabledCommands)
        {
            this._provider = provider;
            this._client = discord;
            this._commands = commands;
            this._logger = logger;
            this._prefixService = prefixService;
            this._disabledCommands = disabledCommands;
        }

        public async Task StartAsync()
        {
            Log.Information("Starting bot");

            var discordToken = ConfigData.Data.Discord.Token; // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
            {
                throw new Exception("Please enter your bots token into the `/configs/config.json` file.");
            }

            await TestLastFmApi();

            Log.Information("Loading all prefixes");
            await this._prefixService.LoadAllPrefixes();

            Log.Information("Loading all disabled commands");
            await this._disabledCommands.LoadAllDisabledCommands();

            Log.Information("Logging into Discord");
            await this._client.LoginAsync(TokenType.Bot, discordToken);


            Log.Information("Setting Discord user status");
            await this._client.SetStatusAsync(UserStatus.DoNotDisturb);

            if (!string.IsNullOrEmpty(ConfigData.Data.Bot.Status))
            {
                Log.Information($"Setting custom status to '{ConfigData.Data.Bot.Status}'");
                await this._client.SetGameAsync(ConfigData.Data.Bot.Status);
            }

            Log.Information("Loading command modules");
            await this._commands
                .AddModulesAsync(
                    Assembly.GetEntryAssembly(),
                    this._provider); // Load commands and modules into the command service

            foreach (var shard in this._client.Shards)
            {
                Log.Information("ShardStartConnection: shard {shardId}", shard.ShardId);
                await shard.StartAsync();
                await Task.Delay(6000);
            }

            Log.Information("Preparing cache folder");
            PrepareCacheFolder();

            await this.StartMetricsServer();
        }


        private async Task TestLastFmApi()
        {
            var fmClient = new LastfmClient(ConfigData.Data.LastFm.Key, ConfigData.Data.LastFm.Secret);

            Log.Information("Testing Last.FM API");
            var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport");

            if (lastFMUser.Status.ToString().Equals("BadApiKey"))
            {
                Log.Fatal("BadLastfmApiKey",
                    "Warning! Invalid API key for Last.FM! Please set the proper API keys in the `/Configs/ConfigData.json`! \n \n" +
                    "Exiting in 5 seconds...");

                Thread.Sleep(5000);
                Environment.Exit(0);
            }
            else
            {
                Log.Information("Last.FM API test successful");
            }
        }


        private Task StartMetricsServer()
        {
            Thread.Sleep(TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds));
            if (this._client == null || this._client.CurrentUser == null)
            {
                Log.Information("Delaying metric server startup");
                Thread.Sleep(TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds));
            }
            Log.Information("Starting metrics server");

            var prometheusPort = 4444;
            if (!this._client.CurrentUser.Id.Equals(Constants.BotProductionId))
            {
                Log.Information("Prometheus port selected is non-production");
                prometheusPort = 4422;
            }

            Log.Information($"Prometheus starting on port {prometheusPort}");

            var server = new MetricServer("localhost", prometheusPort);
            server.Start();

            Log.Information($"Prometheus running on localhost:{prometheusPort}/metrics");
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
