using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Hangfire;
using Hangfire.MemoryStorage;
using IF.Lastfm.Core.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using Prometheus;
using Serilog;

// ReSharper disable MemberCanBePrivate.Global

namespace FMBot.Bot.Services;

public class StartupService
{
    private readonly CommandService<CommandContext> _textCommands;
    private readonly ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext> _appCommands;
    private readonly GuildDisabledCommandService _guildDisabledCommands;
    private readonly ChannelToggledCommandService _channelToggledCommands;
    private readonly DisabledChannelService _disabledChannelService;
    private readonly ShardedGatewayClient  _client;
    private readonly IPrefixService _prefixService;
    private readonly IServiceProvider _provider;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly GuildService _guildService;
    private readonly UserService _userService;
    private readonly TimerService _timerService;
    private readonly SupporterService _supporterService;
    private readonly ChartService _chartService;
    private readonly ShortcutService _shortcutService;

    public StartupService(
        IServiceProvider provider,
        ShardedGatewayClient discord,
        IPrefixService prefixService,
        GuildDisabledCommandService guildDisabledCommands,
        ChannelToggledCommandService channelToggledCommands,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        UserService userService,
        DisabledChannelService disabledChannelService,
        TimerService timerService,
        SupporterService supporterService,
        ChartService chartService,
        ShortcutService shortcutService,
        CommandService<CommandContext> textCommands,
        ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext> appCommands)
    {
        this._provider = provider;
        this._client = discord;
        this._prefixService = prefixService;
        this._guildDisabledCommands = guildDisabledCommands;
        this._channelToggledCommands = channelToggledCommands;
        this._contextFactory = contextFactory;
        this._guildService = guildService;
        this._userService = userService;
        this._disabledChannelService = disabledChannelService;
        this._timerService = timerService;
        this._supporterService = supporterService;
        this._chartService = chartService;
        this._shortcutService = shortcutService;
        this._textCommands = textCommands;
        this._appCommands = appCommands;
        this._botSettings = botSettings.Value;
    }

    public async Task StartAsync()
    {
        await using var context = await this._contextFactory.CreateDbContextAsync();
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
        await this._channelToggledCommands.LoadAllToggledCommands();

        Log.Information("Loading all disabled channels");
        await this._disabledChannelService.LoadAllDisabledChannels();

        Log.Information("Logging into Discord");
        await this._client.StartAsync();

        Log.Information("Loading interaction modules");
        this._appCommands.AddModules(typeof(Program).Assembly);

        Log.Information("Loading command modules");
        this._textCommands.AddModules(typeof(Program).Assembly);

        Log.Information("Preparing cache folder");
        PrepareCacheFolder();

        Log.Information("Downloading chart files");
        await this._chartService.DownloadChartFilesAsync();

        Log.Information("Loading shortcuts");
        await this._shortcutService.LoadAllShortcuts();

        var gateway = await this._client.Rest.GetGatewayBotAsync();
        Log.Information("ShardStarter: connects left {connectsLeft} - reset after {resetAfter}",
            gateway.SessionStartLimit.Remaining, gateway.SessionStartLimit.ResetAfter);

        var maxConcurrency = gateway.SessionStartLimit.MaxConcurrency;
        if (maxConcurrency > 8)
        {
            maxConcurrency = 8;
        }

        // Log.Information("ShardStarter: max concurrency {maxConcurrency}, total shards {shardCount}", maxConcurrency,
        //     this._client.Shards.Count);
        //
        // var connectTasks = new List<Task>();
        // var connectingShards = new List<int>();
        //
        // foreach (var shard in this._client.Shards)
        // {
        //     Log.Information("ShardConnectionStart: shard #{shardId}", shard.ShardId);
        //
        //     connectTasks.Add(shard.StartAsync());
        //     connectingShards.Add(shard.ShardId);
        //
        //     if (connectTasks.Count >= maxConcurrency)
        //     {
        //         await Task.WhenAll(connectTasks);
        //
        //         while (this._client.Shards
        //                .Where(w => connectingShards.Contains(w.ShardId))
        //                .Any(a => a.ConnectionState != ConnectionState.Connected))
        //         {
        //             await Task.Delay(100);
        //         }
        //
        //         Log.Information("ShardStarter: All shards in group concurrently connected");
        //         await Task.Delay(3000);
        //
        //         connectTasks = new();
        //     }
        // }
        //
        // Log.Information("ShardStarter: All connects started, waiting until all are connected");
        // while (this._client.Shards.Any(a => a.ConnectionState != ConnectionState.Connected))
        // {
        //     await Task.Delay(100);
        // }

        await this._client.StartAsync();

        Log.Information("ShardStarter: Done");

        await this._timerService.UpdateStatus();
        await this._timerService.UpdateHealthCheck();

        await this.RegisterSlashCommands();

        InitializeHangfireConfig();
        this._timerService.QueueJobs();

        this.StartMetricsPusher();

        // TODO configure based on shard count
        var startDelay = 10;

        if (ConfigData.Data.Shards == null || ConfigData.Data.Shards.MainInstance == true)
        {
            BackgroundJob.Schedule(() => this.RegisterSlashCommands(), TimeSpan.FromSeconds(startDelay));
            BackgroundJob.Schedule(() => this._supporterService.AddRoleToNewSupporters(), TimeSpan.FromSeconds(10));
        }

        BackgroundJob.Schedule(() => this.CacheSlashCommandIds(), TimeSpan.FromSeconds(startDelay));

        await this.CachePremiumGuilds();
        await this.CacheDiscordUserIds();
    }

    private void InitializeHangfireConfig()
    {
        GlobalConfiguration.Configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSerilogLogProvider()
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMemoryStorage()
            .UseActivator(new HangfireActivator(this._provider));
    }

    private async Task TestLastFmApi()
    {
        var fmClient = new LastfmClient(this._botSettings.LastFm.PublicKey, this._botSettings.LastFm.PublicKeySecret);

        Log.Information("Testing Last.fm API");
        var lastFMUser = await fmClient.User.GetInfoAsync("fm-bot");

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

    private void StartMetricsPusher()
    {
        if (string.IsNullOrWhiteSpace(this._botSettings.Bot.MetricsPusherName) ||
            string.IsNullOrWhiteSpace(this._botSettings.Bot.MetricsPusherEndpoint))
        {
            Log.Information("Metrics pusher config not set, not pushing");
            return;
        }

        Log.Information("Starting metrics pusher");
        var options = new MetricPusherOptions
        {
            Endpoint = this._botSettings.Bot.MetricsPusherEndpoint,
            Job = this._botSettings.Bot.MetricsPusherName,
        };

        if (!string.IsNullOrWhiteSpace(ConfigData.Data.Shards?.InstanceName))
        {
            options.AdditionalLabels = new List<Tuple<string, string>>()
            {
                new("instance", ConfigData.Data.Shards.InstanceName)
            };
        }

        var pusher = new MetricPusher(options);

        pusher.Start();

        Log.Information("Metrics pusher pushing to {MetricsPusherEndpoint}, job name {MetricsPusherName}",
            this._botSettings.Bot.MetricsPusherEndpoint, this._botSettings.Bot.MetricsPusherName);
    }

    public async Task RegisterSlashCommands()
    {
        Log.Information("Starting slash command registration");

        Log.Information("Registering slash commands globally");
        await this._appCommands.RegisterCommandsAsync(this._client.Rest, ConfigData.Data.Discord.ApplicationId.GetValueOrDefault());
    }

    private static void PrepareCacheFolder()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public async Task CacheSlashCommandIds()
    {
        var commands = await this._client.Rest.GetGlobalApplicationCommandsAsync(ConfigData.Data.Discord.ApplicationId.GetValueOrDefault());
        Log.Information("Found {slashCommandCount} registered slash commands", commands.Count);

        foreach (var cmd in commands)
        {
            PublicProperties.SlashCommands.TryAdd(cmd.Name, cmd.Id);
        }
    }

    private async Task CachePremiumGuilds()
    {
        var guilds = await this._guildService.GetPremiumGuilds();
        Log.Information("Found {slashCommandCount} premium servers", guilds.Count);

        foreach (var guild in guilds)
        {
            PublicProperties.PremiumServers.TryAdd(guild.DiscordGuildId, guild.GuildId);
        }
    }

    private async Task CacheDiscordUserIds()
    {
        var users = await this._userService.GetAllDiscordUserIds();
        Log.Information("Found {slashCommandCount} registered users", users.Count);

        foreach (var user in users)
        {
            PublicProperties.RegisteredUsers.TryAdd(user.DiscordUserId, user.UserId);
        }
    }
}
