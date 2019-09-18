using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Handlers;
using FMBot.Bot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FMBot.Bot
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }

        public Startup(string[] args)
        {
            var builder = new ConfigurationBuilder(); // Create a new instance of the config builder
            Configuration = builder.Build(); // Build the configuration
        }

        public static async Task RunAsync(string[] args)
        {
            var startup = new Startup(args);
            await startup.RunAsync();
        }

        public async Task RunAsync()
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
                // Add discord to the collection
                LogLevel = LogSeverity.Verbose, // Tell the logger to give Verbose amount of info
                MessageCacheSize = 0,
            });

            var logger = new Logger.Logger();

            var timerService = new TimerService(discordClient, logger);

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

                //.AddSingleton<LoggingService>()         // Add loggingservice to the collection
                .AddSingleton<Random>() // Add random to the collection
                .AddSingleton(Configuration); // Add the configuration to the collection
        }
    }
}