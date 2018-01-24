

using Discord;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotModules;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    class Program
    {
        private CommandService commands;
        private DiscordSocketClient client;
        private IServiceProvider services;
        private readonly IServiceCollection map = new ServiceCollection();
        private string prefix;

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                if (!Directory.Exists(GlobalVars.UsersFolder))
                {
                    Directory.CreateDirectory(GlobalVars.UsersFolder);
                }

                Console.WriteLine("Initalizing Discord...");
                client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    WebSocketProvider = WS4NetProvider.Instance,
                    LogLevel = LogSeverity.Verbose
                });

                client.Log += Log;

                prefix = cfgjson.CommandPrefix;

                await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶");
                await client.SetStatusAsync(UserStatus.DoNotDisturb);

                Console.WriteLine("Registering Commands and Modules...");
                commands = new CommandService();

                string token = cfgjson.Token; // Remember to keep this private!

                await InstallCommands();

                Console.WriteLine("Logging In...");
                await client.LoginAsync(TokenType.Bot, token);
                await client.StartAsync();

                Console.WriteLine("Logged In.");

                // Block this task until the program is closed.
                await Task.Delay(-1);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to launch. Error: " + e);
            }
        }
        
        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);

            return Task.CompletedTask;
        }

        public async Task InstallCommands()
        {
            map.AddSingleton(new LoggerService(client, Log));
            map.AddSingleton(new ReliabilityService(client));
            map.AddSingleton(new TimerService(client));
            services = map.BuildServiceProvider();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly());

            client.MessageReceived += HandleCommand_MessageReceived;
            client.MessageUpdated += HandleCommand_MessageEdited;
        }

        public async Task HandleCommand_MessageReceived(SocketMessage messageParam)
        {
            await HandleCommand(messageParam);
        }

        public async Task HandleCommand_MessageEdited(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            await HandleCommand(after);
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            var DiscordCaller = message.Author;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!message.HasCharPrefix(Convert.ToChar(prefix), ref argPos)) return;

            // Create a Command Context
            var context = new CommandContext(client, message);

            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await commands.ExecuteAsync(context, argPos, services);
            if (!result.IsSuccess)
            {
                Console.WriteLine("Error - " + result.Error + ": " + result.ErrorReason);
            }
        }
    }
}
