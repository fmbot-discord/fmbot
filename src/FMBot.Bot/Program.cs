using Bot.Logger;
using Bot.Logger.Interfaces;
using Discord;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
using FMBot.Bot.Handlers;
using FMBot.Services;
using IF.Lastfm.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot
{
    internal class Program
    {
        #region FMBot Init

        private CommandService commands;
        private DiscordShardedClient client;
        private IServiceProvider services;
        private ILogger _logger;
        private readonly IServiceCollection map = new ServiceCollection();
        private string prefix;
        private readonly List<string> commandList = new List<string>();

        public static List<DateTimeOffset> stackCooldownTimer = new List<DateTimeOffset>();

        public static List<SocketGuildUser> stackCooldownTarget = new List<SocketGuildUser>();

        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            map.AddScoped<ILogger, Logger>();

            try
            {
                _logger = new Logger();

                string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                _logger.Log(Process.GetCurrentProcess().ProcessName + "FMBot v" + assemblyVersion + " loading...");

                if (!Directory.Exists(GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(GlobalVars.CacheFolder);
                }
                else
                {
                    DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                    GlobalVars.ClearReadOnly(users);
                }

                _logger.Log("Initalizing Last.FM...");
                await TestLastFMAPI().ConfigureAwait(false);

                _logger.Log("Initalizing Discord...");
                client = new DiscordShardedClient(new DiscordSocketConfig
                {
                    WebSocketProvider = WS4NetProvider.Instance,
                    LogLevel = LogSeverity.Verbose,
                });
                
                prefix = ConfigData.Data.CommandPrefix;

                _logger.Log("Registering Commands and Modules...");
                commands = new CommandService(new CommandServiceConfig
                {
                    CaseSensitiveCommands = false
                });

                await InstallCommands(_logger).ConfigureAwait(false);

                _logger.Log("Logging In...");
                await client.LoginAsync(TokenType.Bot, ConfigData.Data.Token);
                await client.StartAsync();

                _logger.Log("Logged In.");

                await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶").ConfigureAwait(false);
                await client.SetStatusAsync(UserStatus.DoNotDisturb).ConfigureAwait(false);
                AppDomain.CurrentDomain.UnhandledException += CatchFatalException;

                // Block this task until the program is closed.
                await Task.Delay(-1).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogException("Failed to launch.", e);
            }
        }

        #endregion

        #region Program Events
        

        public async Task InstallCommands(ILogger logger)
        {
            _logger = logger;

            map.AddSingleton(new ReliabilityService(client, _logger));
            map.AddSingleton(new TimerService(client, _logger));
            map.AddSingleton(new ClientLogHandler(client, _logger));

            services = map.BuildServiceProvider();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services).ConfigureAwait(false);

            client.MessageReceived += HandleCommand_MessageReceived;
            client.MessageUpdated += HandleCommand_MessageEdited;
            client.CurrentUserUpdated += HandleCommand_CurrentUserUpdated;

            TextInfo ti = CultureInfo.CurrentCulture.TextInfo;

            foreach (ModuleInfo module in commands.Modules)
            {
                foreach (CommandInfo cmd in module.Commands)
                {
                    foreach (string alias in cmd.Aliases)
                    {
                        commandList.Add(alias);
                        commandList.Add(alias.ToUpper());
                        commandList.Add(ti.ToTitleCase(alias));
                    }
                }
            }
        }

        public async Task HandleCommand_MessageReceived(SocketMessage messageParam)
        {
            await HandleCommand(messageParam).ConfigureAwait(false);
        }

        public async Task HandleCommand_MessageEdited(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            await HandleCommand(after).ConfigureAwait(false);
        }

        public Task HandleCommand_CurrentUserUpdated(SocketSelfUser before, SocketSelfUser after)
        {
            _logger = new Logger();
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

            _logger.Log($"Status changed from {status_before} to {status_after}");
            return Task.CompletedTask;
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            _logger = new Logger();

            // Don't process the command if it was a System Message
            if (!(messageParam is SocketUserMessage message))
            {
                return;
            }

            // Create a Command Context
            CommandContext context = new CommandContext(client, message);
            SocketUser curUser = context.Client.CurrentUser as SocketUser;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!message.HasCharPrefix(Convert.ToChar(prefix), ref argPos) || message.IsPinned || message.Author.IsBot && message.Author != curUser)
            {
                return;
            }

            string convertedMessage = message.Content.Replace(prefix, "");
            string[] words = convertedMessage.Split(' ');
            List<string> wordlist = words.Where(f => f != null).ToList();
            bool wordinlist = commandList.Intersect(wordlist).Any();

            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            if (wordinlist)
            {
                _ = context.Channel.TriggerTypingAsync();

                if (message.Author is SocketGuildUser DiscordCaller)
                {
                    SocketGuild guild = DiscordCaller.Guild;
                    _ = guild.Id.ToString();

                    if (stackCooldownTarget.Contains(DiscordCaller))
                    {
                        //If they have used this command before, take the time the user last did something, add 3 seconds, and see if it's greater than this very moment.
                        if (stackCooldownTimer[stackCooldownTarget.IndexOf(DiscordCaller)].AddSeconds(2) >= DateTimeOffset.Now)
                        {
                            //If enough time hasn't passed, reply letting them know how much longer they need to wait, and end the code.
                            int secondsLeft = (int)(stackCooldownTimer[stackCooldownTarget.IndexOf(DiscordCaller)].AddSeconds(2) - DateTimeOffset.Now).TotalSeconds;
                            await context.Channel.SendMessageAsync($"Please wait {secondsLeft} seconds before you use that command again!").ConfigureAwait(false);
                            return;
                        }
                        else
                        {
                            //If enough time has passed, set the time for the user to right now.
                            stackCooldownTimer[stackCooldownTarget.IndexOf(DiscordCaller)] = DateTimeOffset.Now;
                        }
                    }
                    else
                    {
                        //If they've never used this command before, add their username and when they just used this command.
                        stackCooldownTarget.Add(DiscordCaller);
                        stackCooldownTimer.Add(DateTimeOffset.Now);
                    }

                    IResult result = await commands.ExecuteAsync(context, argPos, services).ConfigureAwait(false);

                    if (!result.IsSuccess)
                    {
                        _logger.LogError(result.ErrorReason, result.Error.ToString());
                    }
                    else
                    {
                        GlobalVars.CommandExecutions++;
                        GlobalVars.CommandExecutions_Servers++;
                    }
                }
                else
                {
                    IResult result = await commands.ExecuteAsync(context, argPos, services).ConfigureAwait(false);
                    if (!result.IsSuccess)
                    {
                        _logger.LogError(result.ErrorReason, result.Error.ToString());
                    }
                    else
                    {
                        GlobalVars.CommandExecutions++;
                        GlobalVars.CommandExecutions_DMs++;
                    }
                }
            }
        }

        private static void CatchFatalException(object sender, UnhandledExceptionEventArgs t)
        {
            Environment.Exit(1);
        }

        public async Task TestLastFMAPI()
        {
            LastfmClient fmClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

            Console.WriteLine("Checking Last.FM API...");
            var lastFMUser = await fmClient.User.GetInfoAsync("Lastfmsupport").ConfigureAwait(false);

            if (lastFMUser.Status.ToString().Equals("BadApiKey"))
            {
                Console.WriteLine("Warning! Invalid API key for Last.FM! Please set the proper API keys in the Configs/ConfigData.json! \n \n" +
                                  "Exiting in 10 seconds...");

                Thread.Sleep(10000);
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Last.FM API test successful.");
            }
        }

        #endregion
    }
}
