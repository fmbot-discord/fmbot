using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Handlers;
using FMBot.Bot.Services;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;

namespace FMBot.Bot
{
    public class Startup
    {
        private IConfigurationRoot Configuration { get; }

        public Startup(string[] args)
        {
            var builder = new ConfigurationBuilder(); // Create a new instance of the config builder
            Configuration = builder.Build(); // Build the configuration
        }

        public static async Task RunAsync(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
            var startup = new Startup(args);
            await startup.RunAsync();
        }

        private async Task RunAsync()
        {
            var services = new ServiceCollection(); // Create a new instance of a service collection
            ConfigureServices(services);

            var provider = services.BuildServiceProvider(); // Build the service provider
            //provider.GetRequiredService<LoggingService>();      // Start the logging service
            provider.GetRequiredService<CommandHandler>(); // Start the command handler service

            await provider.GetRequiredService<StartupService>().StartAsync(new Logger.Logger()); // Start the startup service
            await Task.Delay(-1); // Keep the program alive
        }

        private void ConfigureServices(IServiceCollection services)
        {
            var discordClient = new DiscordShardedClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 0
            });

            var logger = new Logger.Logger();

            // Timer service (featured)
            var timerService = new TimerService(discordClient, logger);

            using (var context = new FMBotDbContext())
            {
                try
                {
                    logger.Log("Ensuring database is up to date...");
                    context.Database.Migrate();
                }
                catch (Exception e)
                {
                    logger.LogError("Migrations", $"Something went wrong while creating/updating the database! \n{e.Message}");
                    throw;
                }
            }

            services
                .AddSingleton(discordClient)
                .AddSingleton(new CommandService(new CommandServiceConfig
                {
                    // Add the command service to the collection
                    LogLevel = LogSeverity.Verbose, // Tell the logger to give Verbose amount of info
                    DefaultRunMode = RunMode.Async, // Force all commands to run async by default
                }))
                .AddSingleton<CommandHandler>() // Add the command handler to the collection
                .AddSingleton<StartupService>() // Add startupservice to the collection
                .AddSingleton(logger)
                .AddSingleton(timerService)

                .AddSingleton<Random>() // Add random to the collection
                .AddSingleton(Configuration); // Add the configuration to the collection

            _ = StartMetricsServer(discordClient, logger);
        }

        static async Task StartMetricsServer(DiscordShardedClient client, Logger.Logger logger)
        {
            // Wait for login
            await Task.Delay(30000);
            logger.Log("Starting metrics server");

            var prometheusPort = 4444;
            if (client.CurrentUser != null && client.CurrentUser.Username.Contains("develop"))
            {
                prometheusPort = 4422;
            }

            logger.Log($"Prometheus starting on port {prometheusPort}");

            var server = new MetricServer(hostname: "localhost", port: prometheusPort);
            server.Start();

            logger.Log($"Prometheus running on localhost:{prometheusPort}/metrics");
        }

        static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Unhandled exception! \n \n" + e.ExceptionObject + "\n", ConsoleColor.Red);

            var logger = new Logger.Logger();
            logger.Log("UnhandledException! \n \n" + e.ExceptionObject + "\n");
        }
    }
}
