using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading.Tasks;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Hangfire;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Factories;
using FMBot.Persistence.Interfaces;
using System.Net.Http;
using FMBot.Bot.Extensions;
using Web.InternalApi;
using FMBot.AppleMusic;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;
using GatewayIntents = NetCord.Gateway.GatewayIntents;

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
        ConfigureLogging();

        var services = new ServiceCollection();
        ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        InitializeRequiredServices(provider);

        await provider.GetRequiredService<StartupService>().StartAsync();

        StartBackgroundJobServer();

        await Task.Delay(-1); // Keep the program alive
    }

    private void ConfigureLogging()
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
            .Enrich.WithProperty("Environment",
                !string.IsNullOrEmpty(this.Configuration.GetSection("Environment")?.Value)
                    ? this.Configuration.GetSection("Environment").Value
                    : "unknown")
            .Enrich.WithProperty("BotUserId", botUserId)
            .WriteTo.Console(consoleLevel)
            .WriteTo.Seq(this.Configuration.GetSection("Logging:SeqServerUrl")?.Value,
                LogEventLevel.Information,
                apiKey: this.Configuration.GetSection("Logging:SeqApiKey")?.Value)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += AppUnhandledException;

        Log.Information(".fmbot starting up...");
    }

    private static void InitializeRequiredServices(ServiceProvider provider)
    {
        provider.GetRequiredService<CommandHandler>();
        provider.GetRequiredService<InteractionHandler>();
        provider.GetRequiredService<ClientLogHandler>();
        provider.GetRequiredService<UserEventHandler>();
    }

    private static void StartBackgroundJobServer()
    {
        var options = new BackgroundJobServerOptions
        {
            WorkerCount = 64
        };

        _ = new BackgroundJobServer(options);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.Configure<BotSettings>(this.Configuration);

        var discordClient = ConfigureDiscordClient();

        RegisterCoreServices(services, discordClient);
        RegisterHandlerServices(services);
        RegisterBuilderServices(services);
        RegisterDataRepositories(services);
        RegisterFeatureServices(services);
        RegisterHttpClients(services);
        RegisterThirdPartyServices(services);

        services.AddHealthChecks();
        services.AddDbContextFactory<FMBotDbContext>(b =>
            b.UseNpgsql(this.Configuration["Database:ConnectionString"]));
        services.AddMemoryCache(options => { options.TrackStatistics = false; });
    }

    private static ShardedGatewayClient ConfigureDiscordClient()
    {
        const GatewayIntents intents = GatewayIntents.GuildUsers | GatewayIntents.MessageContent |
                                       GatewayIntents.DirectMessages | GatewayIntents.GuildMessages |
                                       GatewayIntents.Guilds |
                                       GatewayIntents.GuildVoiceStates;

        var maxConcurrency = ConfigData.Data.Discord.MaxConcurrency;

        if (ConfigData.Data.Shards != null && ConfigData.Data.Shards.StartShard.HasValue &&
            ConfigData.Data.Shards.EndShard.HasValue)
        {
            var startShard = ConfigData.Data.Shards.StartShard.Value;
            var endShard = ConfigData.Data.Shards.EndShard.Value;

            Log.Warning(
                "Initializing Discord sharded client with {totalShards} total shards, running shards {startingShard} to {endingShard}",
                ConfigData.Data.Shards.TotalShards, startShard, endShard);

            return new ShardedGatewayClient(new BotToken(ConfigData.Data.Discord.Token), new ShardedGatewayClientConfiguration
            {
                IntentsFactory = _ => intents,
                TotalShardCount = ConfigData.Data.Shards.TotalShards,
                ShardRange = startShard..(endShard + 1), // End is exclusive in NetCord
                MaxConcurrency = maxConcurrency
            });
        }

        Log.Warning("Initializing normal Discord sharded client");
        return new ShardedGatewayClient(new BotToken(ConfigData.Data.Discord.Token), new ShardedGatewayClientConfiguration
        {
            IntentsFactory = _ => intents,
            MaxConcurrency = maxConcurrency
        });
    }

    private void RegisterCoreServices(IServiceCollection services, ShardedGatewayClient discordClient)
    {
        services
            .AddSingleton(discordClient)
            .AddSingleton<StartupService>()
            .AddSingleton<Random>()
            .AddSingleton(this.Configuration);

        // NetCord command services
        services.AddSingleton(new CommandService<CommandContext>(CommandServiceConfiguration<CommandContext>.Default with
        {
            // ParameterSeparators = [] // Remove space from separators to allow multi-word aliases like "login discogs"
        }));
        services.AddSingleton<ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext>>();
        services.AddSingleton<ComponentInteractionService<ComponentInteractionContext>>();
        services.AddSingleton<ComponentInteractionService<ModalInteractionContext>>();
    }

    private static void RegisterHandlerServices(IServiceCollection services)
    {
        services
            .AddSingleton<ClientLogHandler>()
            .AddSingleton<CommandHandler>()
            .AddSingleton<InteractionHandler>()
            .AddSingleton<UserEventHandler>();
    }

    private static void RegisterBuilderServices(IServiceCollection services)
    {
        services
            .AddSingleton<AlbumBuilders>()
            .AddSingleton<ArtistBuilders>()
            .AddSingleton<ChartBuilders>()
            .AddSingleton<CountryBuilders>()
            .AddSingleton<CrownBuilders>()
            .AddSingleton<DiscogsBuilder>()
            .AddSingleton<EurovisionBuilders>()
            .AddSingleton<FriendBuilders>()
            .AddSingleton<GameBuilders>()
            .AddSingleton<GenreBuilders>()
            .AddSingleton<GuildBuilders>()
            .AddSingleton<GuildSettingBuilder>()
            .AddSingleton<ImportBuilders>()
            .AddSingleton<PlayBuilder>()
            .AddSingleton<PremiumSettingBuilder>()
            .AddSingleton<RecapBuilders>()
            .AddSingleton<StaticBuilders>()
            .AddSingleton<TemplateBuilders>()
            .AddSingleton<TrackBuilders>()
            .AddSingleton<UserBuilder>()
            .AddSingleton<YoutubeBuilders>();
    }

    private static void RegisterDataRepositories(IServiceCollection services)
    {
        services
            .AddSingleton<AlbumRepository>()
            .AddSingleton<ArtistRepository>()
            .AddSingleton<TrackRepository>()
            .AddSingleton<SmallIndexRepository>()
            .AddSingleton<IDataSourceFactory, DataSourceFactory>()
            .AddSingleton<IPlayDataSourceRepository, PlayDataSourceRepository>()
            .AddSingleton<IPrefixService, PrefixService>()
            .AddSingleton<IUserIndexQueue, UserIndexQueue>()
            .AddSingleton<IUserUpdateQueue, UserUpdateQueue>();
    }

    private static void RegisterFeatureServices(IServiceCollection services)
    {
        // Add general services
        services
            .AddSingleton<AdminService>()
            .AddSingleton<AlbumService>()
            .AddSingleton<AliasService>()
            .AddSingleton<ArtistsService>()
            .AddSingleton<CensorService>()
            .AddSingleton<ChartService>()
            .AddSingleton<CountryService>()
            .AddSingleton<CrownService>()
            .AddSingleton<DiscogsService>()
            .AddSingleton<EurovisionService>()
            .AddSingleton<FeaturedService>()
            .AddSingleton<FriendsService>()
            .AddSingleton<GameService>()
            .AddSingleton<GenreService>()
            .AddSingleton<IndexService, IndexService>()
            .AddSingleton<ImportService>()
            .AddSingleton<MusicBotService>()
            .AddSingleton<MusicDataFactory>()
            .AddSingleton<PlayService>()
            .AddSingleton<PuppeteerService>()
            .AddSingleton<SettingService>()
            .AddSingleton<ShortcutService>()
            .AddSingleton<SupporterService>()
            .AddSingleton<TemplateService>()
            .AddSingleton<TimeService>()
            .AddSingleton<TimerService>()
            .AddSingleton<TrackService>()
            .AddSingleton<UpdateService, UpdateService>()
            .AddSingleton<UserService>()
            .AddSingleton<YoutubeService>();

        // Guild-specific services
        services
            .AddSingleton<GuildService>()
            .AddSingleton<GuildDisabledCommandService>()
            .AddSingleton<ChannelToggledCommandService>()
            .AddSingleton<DisabledChannelService>()
            .AddSingleton<WebhookService>();

        // WhoKnows services
        services
            .AddSingleton<WhoKnowsService>()
            .AddSingleton<WhoKnowsAlbumService>()
            .AddSingleton<WhoKnowsArtistService>()
            .AddSingleton<WhoKnowsPlayService>()
            .AddSingleton<WhoKnowsFilterService>()
            .AddSingleton<WhoKnowsTrackService>();

        // Interactive configuration
        services.Configure<InteractiveServiceOptions>(options =>
        {
            options.ReturnAfterSendingPaginator = true;
            options.ProcessSinglePagePaginators = true;
        });
        services.AddSingleton<InteractiveService>();
    }

    private static void RegisterHttpClients(IServiceCollection services)
    {
        services.AddHttpClient<BotListService>();
        services.AddHttpClient<ILastfmApi, LastfmApi>();
        services.AddHttpClient<ChartService>();
        services.AddHttpClient<ImportService>();
        services.AddHttpClient<DiscogsApi>();
        services.AddHttpClient<GeniusService>();
        services.AddHttpClient<ILastfmRepository, LastFmRepository>();
        services.AddHttpClient<TrackService>();
        services.AddHttpClient<DiscordSkuService>();
        services.AddHttpClient<OpenAiService>();
        services.AddHttpClient<WebhookService>();
        services.AddHttpClient<UserService>();
        services.AddHttpClient<AppleMusicVideoService>();

        services.AddHttpClient<SpotifyService>(client => { client.Timeout = TimeSpan.FromSeconds(10); });

        MetaBrainz.MusicBrainz.Query.DelayBetweenRequests = 1.5;
        MetaBrainz.MusicBrainz.Query.DefaultUserAgent.Add(new ProductInfoHeaderValue("fmbot", "1.0"));
        MetaBrainz.MusicBrainz.Query.DefaultUserAgent.Add(new ProductInfoHeaderValue("(+contact@fm.bot)"));

        services.AddHttpClient("MusicBrainz", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddSingleton<MusicBrainzService>();
    }

    private void RegisterThirdPartyServices(IServiceCollection services)
    {
        ConfigureOpenCollectiveServices(services);

        ConfigureAppleMusicServices(services);

        ConfigureGrpcServices(services);
    }

    private static void ConfigureOpenCollectiveServices(IServiceCollection services)
    {
        services.AddHttpClient("OpenCollective",
            client => { client.BaseAddress = new Uri("https://api.opencollective.com/graphql/v2"); });

        services.AddSingleton(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("OpenCollective");

            return new GraphQLHttpClient(new GraphQLHttpClientOptions
            {
                EndPoint = new Uri("https://api.opencollective.com/graphql/v2")
            }, new SystemTextJsonSerializer(options => options.PropertyNameCaseInsensitive = true), httpClient);
        });

        services.AddSingleton<OpenCollectiveService>();
    }

    private static void ConfigureAppleMusicServices(IServiceCollection services)
    {
        services.AddSingleton<AppleMusicService>();
        services.AddSingleton<AppleMusicJwtAuthProvider>(provider =>
            new AppleMusicJwtAuthProvider(
                ConfigData.Data.AppleMusic.Secret,
                ConfigData.Data.AppleMusic.KeyId,
                ConfigData.Data.AppleMusic.TeamId));

        services.AddHttpClient<AppleMusicApi>((provider, client) =>
        {
            var authProvider = provider.GetRequiredService<AppleMusicJwtAuthProvider>();
            var authHeader = authProvider.CreateAuthorizationHeaderAsync().Result;
            client.DefaultRequestHeaders.Add("Authorization", authHeader);
            client.BaseAddress = new Uri("https://api.music.apple.com/v1/catalog/us/");
        });

        services.AddHttpClient<AppleMusicAltApi>((provider, client) =>
        {
            var authProvider = provider.GetRequiredService<PuppeteerService>();
            var authHeader = authProvider.GetAppleToken().Result;
            if (authHeader != null)
            {
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("Origin", "https://music.apple.com");
                client.DefaultRequestHeaders.Add("Referer", "https://music.apple.com");
            }
            else
            {
                Log.Warning("No alt Apple Music auth header");
            }

            client.BaseAddress = new Uri("https://amp-api.music.apple.com/v1/catalog/us/");
        });
    }

    private void ConfigureGrpcServices(IServiceCollection services)
    {
        services.AddConfiguredGrpcClient<TimeEnrichment.TimeEnrichmentClient>(this.Configuration);
        services.AddConfiguredGrpcClient<StatusHandler.StatusHandlerClient>(this.Configuration);
        services.AddConfiguredGrpcClient<CensorHandler.CensorHandlerClient>(this.Configuration);
        services.AddConfiguredGrpcClient<AlbumEnrichment.AlbumEnrichmentClient>(this.Configuration);
        services.AddConfiguredGrpcClient<ArtistEnrichment.ArtistEnrichmentClient>(this.Configuration);
        services.AddConfiguredGrpcClient<TrackEnrichment.TrackEnrichmentClient>(this.Configuration);
        services.AddConfiguredGrpcClient<SupporterLinkService.SupporterLinkServiceClient>(this.Configuration);
        services.AddConfiguredGrpcClient<EurovisionEnrichment.EurovisionEnrichmentClient>(this.Configuration);
    }


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
