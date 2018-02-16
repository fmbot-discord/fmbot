using Discord;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Reflection;
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

                Console.WriteLine("Registering Commands and Modules...");
                commands = new CommandService();

                string token = cfgjson.Token; // Remember to keep this private!

                await InstallCommands();

                Console.WriteLine("Logging In...");
                await client.LoginAsync(TokenType.Bot, token);
                await client.StartAsync();

                Console.WriteLine("Logged In.");

                await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶");
                await client.SetStatusAsync(UserStatus.DoNotDisturb);

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
            map.AddSingleton(new ReliabilityService(client, Log));
            map.AddSingleton(new TimerService(client));
            services = map.BuildServiceProvider();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly());

            client.MessageReceived += HandleCommand_MessageReceived;
            //client.MessageUpdated += HandleCommand_MessageEdited;
            client.CurrentUserUpdated += HandleCommand_CurrentUserUpdated;
        }

        public async Task HandleCommand_MessageReceived(SocketMessage messageParam)
        {
            await HandleCommand(messageParam);
        }

        public async Task HandleCommand_MessageEdited(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            await HandleCommand(after);
        }

        public Task HandleCommand_CurrentUserUpdated(SocketSelfUser before, SocketSelfUser after)
        {
            string status_after = "Online";
            string status_before = "Online";

            switch (before.Status)
            {
                case UserStatus.Offline: status_before = "Offline"; break;
                case UserStatus.Online: status_before = "Online"; break;
                case UserStatus.Idle: status_before = "Idle"; break;
                case UserStatus.AFK: status_before = "AFK"; break;
                case UserStatus.DoNotDisturb: status_before = "Do Not Disturb"; break;
                case UserStatus.Invisible: status_before = "Invisible/Offline"; break;
            }

            switch (after.Status)
            {
                case UserStatus.Offline: status_after = "Offline"; break;
                case UserStatus.Online: status_after = "Online"; break;
                case UserStatus.Idle: status_after = "Idle"; break;
                case UserStatus.AFK: status_after = "AFK"; break;
                case UserStatus.DoNotDisturb: status_after = "Do Not Disturb"; break;
                case UserStatus.Invisible: status_after = "Invisible/Offline"; break;
            }

            Console.WriteLine("Status of bot changed from " + status_before + " to " + status_after);

            return Task.CompletedTask;
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!message.HasCharPrefix(Convert.ToChar(prefix), ref argPos)) return;


            // Create a Command Context
            var context = new CommandContext(client, message);

            var DiscordCaller = message.Author as SocketGuildUser;
            string callerid = DiscordCaller.Id.ToString();
            string callerserverid = DiscordCaller.Guild.Id.ToString();
            bool isonblacklist = DBase.IsUserOnBlacklist(callerid, callerserverid);

            if (isonblacklist == true)
            {
                await context.Channel.SendMessageAsync("You have been blacklisted from the server. Please contact a server moderator or administrator if you have any questions regarding this decision.");
                return;
            }

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
