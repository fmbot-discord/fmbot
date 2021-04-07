using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BotListAPI;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Domain;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;

namespace FMBot.Bot.Services
{
    public class StartupService
    {
        private readonly CommandService _commands;
        private readonly IGuildDisabledCommandService _guildDisabledCommands;
        private readonly IChannelDisabledCommandService _channelDisabledCommands;
        private readonly DiscordShardedClient _client;
        private readonly IPrefixService _prefixService;
        private readonly IServiceProvider _provider;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;


        public StartupService(
            IServiceProvider provider,
            DiscordShardedClient discord,
            CommandService commands,
            IPrefixService prefixService,
            IGuildDisabledCommandService guildDisabledCommands,
            IChannelDisabledCommandService channelDisabledCommands,
            IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._provider = provider;
            this._client = discord;
            this._commands = commands;
            this._prefixService = prefixService;
            this._guildDisabledCommands = guildDisabledCommands;
            this._channelDisabledCommands = channelDisabledCommands;
            this._contextFactory = contextFactory;
        }

        public async Task StartAsync()
        {
            await using var context = this._contextFactory.CreateDbContext();
            try
            {
                Log.Information("Ensuring database is up to date");
                await context.Database.MigrateAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while creating/updating the database!");
                throw;
            }

            Log.Information("Starting bot");

            var discordToken = ConfigData.Data.Discord.Token; // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
            {
                throw new Exception("Please enter your bots token into the `/configs/config.json` file.");
            }

            await TestLastFmApi();

            Log.Information("Loading all prefixes");
            await this._prefixService.LoadAllPrefixes();

            Log.Information("Loading all server disabled commands");
            await this._guildDisabledCommands.LoadAllDisabledCommands();

            Log.Information("Loading all channel disabled commands");
            await this._channelDisabledCommands.LoadAllDisabledCommands();

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

            var shardTimeOut = 4500;
            foreach (var shard in this._client.Shards)
            {
                Log.Information("ShardStartConnection: shard {shardId}", shard.ShardId);
                await shard.StartAsync();
                await Task.Delay(shardTimeOut);
                shardTimeOut += 100;
            }

            Log.Information("Preparing cache folder");
            PrepareCacheFolder();

            await this.StartMetricsServer();
            await this.StartBotSiteUpdater();
        }


        private async Task TestLastFmApi()
        {
            var fmClient = new LastfmClient(ConfigData.Data.LastFm.Key, ConfigData.Data.LastFm.Secret);

            Log.Information("Testing Last.fm API");
            var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport");

            if (lastFMUser.Status.ToString().Equals("BadApiKey"))
            {
                Log.Fatal("BadLastfmApiKey",
                    "Warning! Invalid API key for Last.fm! Please set the proper API keys in the `/Configs/ConfigData.json`! \n \n" +
                    "Exiting in 5 seconds...");

                Thread.Sleep(5000);
                Environment.Exit(0);
            }
            else
            {
                Log.Information("Last.fm API test successful");
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

        private Task StartBotSiteUpdater()
        {
            Thread.Sleep(TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds));

            if (!this._client.CurrentUser.Id.Equals(Constants.BotProductionId))
            {
                Log.Information("Cancelled botlist updater, non-production bot detected");
                return Task.CompletedTask;
            }

            Log.Information("Starting botlist updater");

            var listConfig = new ListConfig();

            if (ConfigData.Data.BotLists != null)
            {
                if (!string.IsNullOrWhiteSpace(ConfigData.Data.BotLists.TopGgApiToken))
                {
                    listConfig.TopGG = ConfigData.Data.BotLists.TopGgApiToken;
                }
                if (!string.IsNullOrWhiteSpace(ConfigData.Data.BotLists.BotsForDiscordToken))
                {
                    listConfig.BotsForDiscord = ConfigData.Data.BotLists.BotsForDiscordToken;
                }
                if (!string.IsNullOrWhiteSpace(ConfigData.Data.BotLists.DiscordBoatsToken))
                {
                    listConfig.DiscordBoats = ConfigData.Data.BotLists.DiscordBoatsToken;
                }
                if (!string.IsNullOrWhiteSpace(ConfigData.Data.BotLists.BotsOnDiscordToken))
                {
                    listConfig.BotsOnDiscord = ConfigData.Data.BotLists.BotsOnDiscordToken;
                }
            }
            else
            {
                Log.Information("Cancelled botlist updater, no botlist tokens in config");
                return Task.CompletedTask;
            }

            try
            {
                var listClient = new ListClient(this._client, listConfig);

                listClient.Start();
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception while attempting to start botlist updater!");
            }

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
