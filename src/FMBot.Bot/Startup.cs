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
using FMBot.Bot.Services.WhoKnows;
using FMBot.LastFM.Services;
using FMBot.Persistence.EntityFrameWork;
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
        private IConfigurationRoot Configuration { get; }

        public Startup(string[] args)
        {
            var config = ConfigData.Data;

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory() + "/configs")
                .AddJsonFile("config.json", true);

            this.Configuration = builder.Build(); // Build the configuration
        }

        public static async Task RunAsync(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty("Environment", !string.IsNullOrEmpty(ConfigData.Data.Environment) ? ConfigData.Data.Environment : "unknown")
                .Enrich.WithProperty("BotUserId", ConfigData.Data.Discord.BotUserId ?? 0)
                .WriteTo.Console()
                .WriteTo.Seq("http://localhost:5341")
                // https://github.com/CXuesong/Serilog.Sinks.Discord/issues/3
                //.WriteTo.Conditional(evt =>
                //        !string.IsNullOrEmpty(ConfigData.Data.Bot.ExceptionChannelWebhookUrl),
                //        wt => wt.Discord(new DiscordWebhookMessenger(ConfigData.Data.Bot.ExceptionChannelWebhookUrl), LogEventLevel.Warning))
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += AppUnhandledException;

            Log.Information(".fmbot starting up...");

            var startup = new Startup(args);
            await startup.RunAsync();
        }

        private async Task RunAsync()
        {
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
                MessageCacheSize = 0,
                AlwaysDownloadUsers = true
            });

            services
                .AddSingleton(discordClient)
                .AddSingleton(new CommandService(new CommandServiceConfig
                {
                    LogLevel = LogSeverity.Verbose,
                    DefaultRunMode = RunMode.Async,
                }))
                .AddSingleton<AdminService>()
                .AddSingleton<ArtistsService>()
                .AddSingleton<CensorService>()
                .AddSingleton<ClientLogHandler>()
                .AddSingleton<CommandHandler>()
                .AddSingleton<FriendsService>()
                .AddSingleton<GeniusService>()
                .AddSingleton<GuildService>()
                .AddSingleton<IChartService, ChartService>()
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
                .AddSingleton<UserEventHandler>()
                .AddSingleton<UserService>()
                .AddSingleton<WhoKnowsAlbumService>()
                .AddSingleton<WhoKnowsArtistService>()
                .AddSingleton<WhoKnowsPlayService>()
                .AddSingleton<WhoKnowsService>()
                .AddSingleton<WhoKnowsTrackService>()
                .AddSingleton<YoutubeService>() // Add random to the collection
                .AddSingleton(this.Configuration) // Add the configuration to the collection
                .AddHttpClient();

            // These services can only be added after the config is loaded
            services
                .AddSingleton<GlobalIndexService>()
                .AddSingleton<GlobalUpdateService>()
                .AddSingleton<IUpdateService, UpdateService>()
                .AddTransient<ILastfmApi, LastfmApi>()
                .AddTransient<LastFmService>();

            services.AddDbContextFactory<FMBotDbContext>(b =>
                b.UseNpgsql(ConfigData.Data.Database.ConnectionString));

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
