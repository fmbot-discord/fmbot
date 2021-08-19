using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Handlers;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Api;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using FMBot.Youtube.Services;
using Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Exceptions;

namespace FMBot.Bot
{
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

            Log.Logger = new LoggerConfiguration()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty("Environment", !string.IsNullOrEmpty(this.Configuration.GetSection("Environment")?.Value) ? this.Configuration.GetSection("Environment").Value : "unknown")
                .Enrich.WithProperty("BotUserId", botUserId)
                .WriteTo.Console()
                .WriteTo.Seq(this.Configuration.GetSection("Logging:SeqServerUrl")?.Value, apiKey: this.Configuration.GetSection("Logging:SeqApiKey")?.Value)
                // https://github.com/CXuesong/Serilog.Sinks.Discord/issues/3
                //.WriteTo.Conditional(evt =>
                //        !string.IsNullOrEmpty(ConfigData.Data.Bot.ExceptionChannelWebhookUrl),
                //        wt => wt.Discord(new DiscordWebhookMessenger(ConfigData.Data.Bot.ExceptionChannelWebhookUrl), LogEventLevel.Warning))
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += AppUnhandledException;

            Log.Information(".fmbot starting up...");

            var services = new ServiceCollection(); // Create a new instance of a service collection
            this.ConfigureServices(services);

            var provider = services.BuildServiceProvider(); // Build the service provider
            //provider.GetRequiredService<LoggingService>();      // Start the logging service
            provider.GetRequiredService<CommandHandler>();
            provider.GetRequiredService<ClientLogHandler>();
            provider.GetRequiredService<UserEventHandler>();

            await provider.GetRequiredService<StartupService>().StartAsync(); // Start the startup service
            await Task.Delay(-1); // Keep the program alive
        }

        private void ConfigureServices(IServiceCollection services)
        {
            var discordClient = new DiscordShardedClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 0
            });

            services.Configure<BotSettings>(this.Configuration);

            services
                .AddSingleton(discordClient)
                .AddSingleton(new CommandService(new CommandServiceConfig
                {
                    LogLevel = LogSeverity.Verbose,
                    DefaultRunMode = RunMode.Async,
                }))
                .AddSingleton<AlbumService>()
                .AddSingleton<AlbumRepository>()
                .AddSingleton<AdminService>()
                .AddSingleton<ArtistsService>()
                .AddSingleton<ArtistRepository>()
                .AddSingleton<CensorService>()
                .AddSingleton<CrownService>()
                .AddSingleton<ClientLogHandler>()
                .AddSingleton<CommandHandler>()
                .AddSingleton<FriendsService>()
                .AddSingleton<GeniusService>()
                .AddSingleton<GenreService>()
                .AddSingleton<GuildService>()
                .AddSingleton<ChartService>()
                .AddSingleton<IGuildDisabledCommandService, GuildDisabledCommandService>()
                .AddSingleton<IChannelDisabledCommandService, ChannelDisabledCommandService>()
                .AddSingleton<IIndexService, IndexService>()
                .AddSingleton<IPrefixService, PrefixService>()
                .AddSingleton<InteractivityService>()
                .AddSingleton<IUserIndexQueue, UserIndexQueue>()
                .AddSingleton<IUserUpdateQueue, UserUpdateQueue>()
                .AddSingleton<PlayService>()
                .AddSingleton<Random>()
                .AddSingleton<SettingService>()
                .AddSingleton<SpotifyService>()
                .AddSingleton<StartupService>()
                .AddSingleton<SupporterService>()
                .AddSingleton<TimerService>()
                .AddSingleton<TimeService>()
                .AddSingleton<MusicBotService>()
                .AddSingleton<TrackService>()
                .AddSingleton<TrackRepository>()
                .AddSingleton<UserEventHandler>()
                .AddSingleton<UserService>()
                .AddSingleton<WebhookService>()
                .AddSingleton<WhoKnowsService>()
                .AddSingleton<WhoKnowsAlbumService>()
                .AddSingleton<WhoKnowsArtistService>()
                .AddSingleton<WhoKnowsPlayService>()
                .AddSingleton<WhoKnowsTrackService>()
                .AddSingleton<YoutubeService>() // Add random to the collection
                .AddSingleton<IConfiguration>(this.Configuration) // Add the configuration to the collection
                .AddHttpClient();

            // These services can only be added after the config is loaded
            services
                .AddSingleton<InteractivityService>()
                .AddSingleton(new InteractivityConfig { DefaultTimeout = TimeSpan.FromMinutes(3) }) 
                .AddSingleton<IndexRepository>()
                .AddSingleton<UpdateRepository>()
                .AddSingleton<IUpdateService, UpdateService>()
                .AddTransient<ILastfmApi, LastfmApi>()
                .AddTransient<LastFmRepository>()
                .AddTransient<InvidiousApi>();

            services.AddHealthChecks();

            services.AddDbContextFactory<FMBotDbContext>(b =>
                b.UseNpgsql(this.Configuration["Database:ConnectionString"]));

            services.AddMemoryCache();
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
}
