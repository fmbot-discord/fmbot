using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BotListAPI;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;

namespace FMBot.Bot.Services;

public class StartupService
{
    private readonly CommandService _commands;
    private readonly InteractionService _interactions;
    private readonly IGuildDisabledCommandService _guildDisabledCommands;
    private readonly IChannelDisabledCommandService _channelDisabledCommands;
    private readonly DiscordShardedClient _client;
    private readonly IPrefixService _prefixService;
    private readonly IServiceProvider _provider;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly InteractionService _interactionService;


    public StartupService(
        IServiceProvider provider,
        DiscordShardedClient discord,
        CommandService commands,
        IPrefixService prefixService,
        IGuildDisabledCommandService guildDisabledCommands,
        IChannelDisabledCommandService channelDisabledCommands,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IOptions<BotSettings> botSettings,
        InteractionService interactionService,
        InteractionService interactions)
    {
        this._provider = provider;
        this._client = discord;
        this._commands = commands;
        this._prefixService = prefixService;
        this._guildDisabledCommands = guildDisabledCommands;
        this._channelDisabledCommands = channelDisabledCommands;
        this._contextFactory = contextFactory;
        this._interactionService = interactionService;
        this._interactions = interactions;
        this._botSettings = botSettings.Value;
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

        var discordToken = this._botSettings.Discord.Token; // Get the discord token from the config file
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

        if (!string.IsNullOrEmpty(this._botSettings.Bot.Status))
        {
            Log.Information($"Setting custom status to '{this._botSettings.Bot.Status}'");
            await this._client.SetGameAsync(this._botSettings.Bot.Status);
        }

        Log.Information("Loading command modules");
        await this._commands
            .AddModulesAsync(
                Assembly.GetEntryAssembly(),
                this._provider);

        Log.Information("Loading slash command modules");
        await this._interactions
            .AddModulesAsync(
                Assembly.GetEntryAssembly(),
                this._provider);

        var shardTimeOut = 3500;
        foreach (var shard in this._client.Shards)
        {
            Log.Information("ShardStartConnection: shard {shardId}", shard.ShardId);
            await shard.StartAsync();
            await Task.Delay(shardTimeOut);
        }

        Log.Information("Preparing cache folder");
        PrepareCacheFolder();

        await this.StartMetricsPusher();
        await this.RegisterSlashCommands();
        await this.StartBotSiteUpdater();
    }

    private async Task TestLastFmApi()
    {
        var fmClient = new LastfmClient(this._botSettings.LastFm.PublicKey, this._botSettings.LastFm.PublicKeySecret);

        Log.Information("Testing Last.fm API");
        var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport");

        if (lastFMUser.Status.ToString().Equals("BadApiKey"))
        {
            Log.Fatal("BadLastfmApiKey\n" +
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

    private Task StartMetricsPusher()
    {
        if (string.IsNullOrWhiteSpace(this._botSettings.Bot.MetricsPusherName) || string.IsNullOrWhiteSpace(this._botSettings.Bot.MetricsPusherEndpoint))
        {
            Log.Information("Metrics pusher config not set, not pushing");
            return Task.CompletedTask;
        }

        Log.Information("Starting metrics pusher");
        var pusher = new MetricPusher(new MetricPusherOptions
        {
            Endpoint = this._botSettings.Bot.MetricsPusherEndpoint,
            Job = this._botSettings.Bot.MetricsPusherName
        });

        pusher.Start();

        Log.Information("Metrics pusher pushing to {MetricsPusherEndpoint}, job name {MetricsPusherName}", this._botSettings.Bot.MetricsPusherEndpoint, this._botSettings.Bot.MetricsPusherName);
        return Task.CompletedTask;
    }

    private async Task RegisterSlashCommands()
    {
        Thread.Sleep(TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds));
        if (this._client == null || this._client.CurrentUser == null)
        {
            Log.Information("Delaying slash command registration");
            Thread.Sleep(TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds));
        }

        Log.Information("Starting slash command registration");

#if DEBUG
        Log.Information("Registering slash commands to guild");
        await this._interactionService.RegisterCommandsToGuildAsync(this._botSettings.Bot.BaseServerId);
#else
            Log.Information("Registering slash commands globally");
            await this._interactionService.RegisterCommandsGloballyAsync();
#endif
    }

    private Task StartBotSiteUpdater()
    {
        Thread.Sleep(TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds));

        if (!this._client.CurrentUser.Id.Equals(Constants.BotProductionId))
        {
            Log.Information("Cancelled botlist updater, non-production bot detected");
            return Task.CompletedTask;
        }

        Log.Information("Starting botlist updater");

        var listConfig = new ListConfig();

        if (this._botSettings.BotLists != null)
        {
            if (!string.IsNullOrWhiteSpace(this._botSettings.BotLists.TopGgApiToken))
            {
                listConfig.TopGG = this._botSettings.BotLists.TopGgApiToken;
            }
            if (!string.IsNullOrWhiteSpace(this._botSettings.BotLists.BotsForDiscordToken))
            {
                listConfig.BotsForDiscord = this._botSettings.BotLists.BotsForDiscordToken;
            }
            if (!string.IsNullOrWhiteSpace(this._botSettings.BotLists.DiscordBoatsToken))
            {
                listConfig.DiscordBoats = this._botSettings.BotLists.DiscordBoatsToken;
            }
            if (!string.IsNullOrWhiteSpace(this._botSettings.BotLists.BotsOnDiscordToken))
            {
                listConfig.BotsOnDiscord = this._botSettings.BotLists.BotsOnDiscordToken;
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
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
