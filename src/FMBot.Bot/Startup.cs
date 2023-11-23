using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Configurations;
using FMBot.Bot.Handlers;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Discogs.Apis;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Api;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using FMBot.Subscriptions.Services;
using FMBot.Youtube.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using RunMode = Discord.Commands.RunMode;
using Hangfire;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Factories;
using FMBot.Domain.Enums;
using FMBot.Persistence.Interfaces;
using System.Linq;

namespace FMBot.Bot;

public class Startup
{
    public Startup(string[] args)
    {
        var config = ConfigData.Data;

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "configs"))
            .AddJsonFile("config.json", true, true)
            .AddEnvironmentVariables();

        this.Configuration = configBuilder.Build();
    }

    public IConfiguration Configuration { get; }

    public static async Task RunAsync(string[] args)
    {
        var startup = new Startup(args);

        await startup.RunAsync();
    }

    private async Task RunAsync()
    {
        var botUserId = long.Parse(this.Configuration.GetSection("Discord:BotUserId")?.Value ?? "0");

        var consoleLevel = LogEventLevel.Warning;
        var logLevel = LogEventLevel.Information;
#if DEBUG
        consoleLevel = LogEventLevel.Verbose;
        logLevel = LogEventLevel.Information;
#endif

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .Enrich.WithExceptionDetails()
            .Enrich.WithEnvironmentVariable("INSTANCE_NAME")
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Environment", !string.IsNullOrEmpty(this.Configuration.GetSection("Environment")?.Value) ? this.Configuration.GetSection("Environment").Value : "unknown")
            .Enrich.WithProperty("BotUserId", botUserId)
            .WriteTo.Console(consoleLevel)
            .WriteTo.Seq(this.Configuration.GetSection("Logging:SeqServerUrl")?.Value, LogEventLevel.Information, apiKey: this.Configuration.GetSection("Logging:SeqApiKey")?.Value)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += AppUnhandledException;
             
        Log.Information(".fmbot starting up...");

        var services = new ServiceCollection(); // Create a new instance of a service collection
        this.ConfigureServices(services);

        var provider = services.BuildServiceProvider(); // Build the service provider
        //provider.GetRequiredService<LoggingService>();      // Start the logging service
        provider.GetRequiredService<CommandHandler>();
        provider.GetRequiredService<InteractionHandler>();
        provider.GetRequiredService<ClientLogHandler>();
        provider.GetRequiredService<UserEventHandler>();

        await provider.GetRequiredService<StartupService>().StartAsync(); // Start the startup service

        // Start hangfire
        using var server = new BackgroundJobServer();

        await Task.Delay(-1); // Keep the program alive
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.Configure<BotSettings>(this.Configuration);

        var config = new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            MessageCacheSize = 0,
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.MessageContent |
                             GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds |
                             GatewayIntents.GuildVoiceStates,
            TotalShards = ConfigData.Data.Shards?.TotalShards != null ? ConfigData.Data.Shards.TotalShards : null,
            ConnectionTimeout = 60000
        };

        DiscordShardedClient discordClient;

        if (ConfigData.Data.Shards != null && ConfigData.Data.Shards.StartShard.HasValue && ConfigData.Data.Shards.EndShard.HasValue)
        {
            var startShard = ConfigData.Data.Shards.StartShard.Value;
            var endShard = ConfigData.Data.Shards.EndShard.Value;

            var arrayLength = endShard - startShard + 1;

            var shards = Enumerable.Range(startShard, arrayLength).ToArray();

            Log.Warning("Initializing Discord sharded client with {totalShards} total shards, starting at shard {startingShard} til {endingShard} - {shards}",
                ConfigData.Data.Shards.TotalShards, startShard, endShard, shards);

            discordClient = new DiscordShardedClient(shards, config);
        }
        else
        {
            Log.Warning("Initializing normal Discord sharded client");

            discordClient = new DiscordShardedClient(config);
        }

        services
            .AddSingleton(discordClient)
            .AddSingleton(new CommandService(new CommandServiceConfig
            {
                LogLevel = LogSeverity.Info,
                DefaultRunMode = RunMode.Async,
            }))
            .AddSingleton<InteractionService>()
            .AddSingleton<AlbumService>()
            .AddSingleton<AlbumBuilders>()
            .AddSingleton<AliasService>()
            .AddSingleton<ArtistBuilders>()
            .AddSingleton<AlbumRepository>()
            .AddSingleton<AdminService>()
            .AddSingleton<ArtistsService>()
            .AddSingleton<ArtistRepository>()
            .AddSingleton<CensorService>()
            .AddSingleton<CrownBuilders>()
            .AddSingleton<CrownService>()
            .AddSingleton<ChartBuilders>()
            .AddSingleton<CountryService>()
            .AddSingleton<CountryBuilders>()
            .AddSingleton<ClientLogHandler>()
            .AddSingleton<CommandHandler>()
            .AddSingleton<DiscogsBuilder>()
            .AddSingleton<DiscogsService>()
            .AddSingleton<FeaturedService>()
            .AddSingleton<FriendsService>()
            .AddSingleton<FriendBuilders>()
            .AddSingleton<GeniusService>()
            .AddSingleton<GenreBuilders>()
            .AddSingleton<GenreService>()
            .AddSingleton<GuildBuilders>()
            .AddSingleton<GuildService>()
            .AddSingleton<GuildSettingBuilder>()
            .AddSingleton<GuildDisabledCommandService>()
            .AddSingleton<ChannelDisabledCommandService>()
            .AddSingleton<DisabledChannelService>()
            .AddSingleton<IIndexService, IndexService>()
            .AddSingleton<IPrefixService, PrefixService>()
            .AddSingleton<ImportBuilders>()
            .AddSingleton<InteractiveService>()
            .AddSingleton<IUserIndexQueue, UserIndexQueue>()
            .AddSingleton<IUserUpdateQueue, UserUpdateQueue>()
            .AddSingleton<PlayService>()
            .AddSingleton<PlayBuilder>()
            .AddSingleton<PremiumSettingBuilder>()
            .AddSingleton<PuppeteerService>()
            .AddSingleton<Random>()
            .AddSingleton<StaticBuilders>()
            .AddSingleton<SettingService>()
            .AddSingleton<StartupService>()
            .AddSingleton<SupporterService>()
            .AddSingleton<TimerService>()
            .AddSingleton<TimeService>()
            .AddSingleton<MusicBotService>()
            .AddSingleton<TrackBuilders>()
            .AddSingleton<TrackRepository>()
            .AddSingleton<UserEventHandler>()
            .AddSingleton<UserBuilder>()
            .AddSingleton<UserService>()
            .AddSingleton<WebhookService>()
            .AddSingleton<WhoKnowsService>()
            .AddSingleton<WhoKnowsAlbumService>()
            .AddSingleton<WhoKnowsArtistService>()
            .AddSingleton<WhoKnowsPlayService>()
            .AddSingleton<WhoKnowsFilterService>()
            .AddSingleton<WhoKnowsTrackService>()
            .AddSingleton<YoutubeService>()
            .AddSingleton<IUpdateService, UpdateService>()
            .AddSingleton<IDataSourceFactory, DataSourceFactory>()
            .AddSingleton<IPlayDataSourceRepository, PlayDataSourceRepository>()
            .AddSingleton<IConfiguration>(this.Configuration);

        // These services can only be added after the config is loaded
        services
            .AddSingleton<InteractionHandler>()
            .AddSingleton<SmallIndexRepository>();

        services.AddHttpClient<ILastfmApi, LastfmApi>();
        services.AddHttpClient<ChartService>();
        services.AddHttpClient<InvidiousApi>();
        services.AddHttpClient<ImportService>();
        services.AddHttpClient<DiscogsApi>();
        services.AddHttpClient<ILastfmRepository, LastFmRepository>();
        services.AddHttpClient<TrackService>();
        services.AddHttpClient<DiscordSkuService>();
        services.AddHttpClient<OpenCollectiveService>();
        services.AddHttpClient<OpenAiService>();

        services.AddHttpClient<SpotifyService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<MusicBrainzService>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("fmbot-discord", "1.0"));
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHealthChecks();

        services.AddDbContextFactory<FMBotDbContext>(b =>
            b.UseNpgsql(this.Configuration["Database:ConnectionString"]));

        services.AddMemoryCache();
    }

    public delegate IPlayDataSourceRepository ServiceResolver(DataSource key);

    private static void AppUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (Log.Logger != null && e.ExceptionObject is Exception exception)
        {
            UnhandledExceptions(exception);

            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
            }
        }
    }

    private static void UnhandledExceptions(Exception e)
    {
        Log.Logger?.Error(e, ".fmbot crashed");
    }
}
