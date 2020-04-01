using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Handlers;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

            var logger = new Logger.Logger();

            services
                .AddSingleton(discordClient)
                .AddSingleton(new CommandService(new CommandServiceConfig
                {
                    LogLevel = LogSeverity.Verbose,
                    DefaultRunMode = RunMode.Async,
                }))
                .AddSingleton<CommandHandler>()
                .AddSingleton<StartupService>()
                .AddSingleton<TimerService>()
                .AddSingleton<IUserIndexQueue, UserIndexQueue>()
                .AddSingleton<IndexService>()
                .AddSingleton(logger)
                .AddSingleton<Random>() // Add random to the collection
                .AddSingleton(Configuration); // Add the configuration to the collection

            using (var context = new FMBotDbContext())
            {
                try
                {
                    logger.Log("Ensuring database is up to date");
                    context.Database.Migrate();
                }
                catch (Exception e)
                {
                    logger.LogError("Migrations", $"Something went wrong while creating/updating the database! \n{e.Message}");
                    throw;
                }
            }
        }

        static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Unhandled exception! \n \n" + e.ExceptionObject + "\n", ConsoleColor.Red);

            var logger = new Logger.Logger();
            logger.Log("UnhandledException! \n \n" + e.ExceptionObject + "\n");
        }
    }
}
