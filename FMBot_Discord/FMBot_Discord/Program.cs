using Discord;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
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
using System.Threading.Tasks;
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
        private readonly IServiceCollection map = new ServiceCollection();
        private string prefix;
        private readonly List<string> commandList = new List<string>();

        private readonly GuildService guildService = new GuildService();

        private readonly UserService userService = new UserService();

        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            try
            {
                string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "FMBot v" + assemblyVersion + " loading..."));

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                if (!Directory.Exists(GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(GlobalVars.CacheFolder);
                }
                else
                {
                    DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                    GlobalVars.ClearReadOnly(users);
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Initalizing Last.FM..."));
                await TestLastFMAPI();

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Initalizing Discord..."));
                client = new DiscordShardedClient(new DiscordSocketConfig
                {
                    WebSocketProvider = WS4NetProvider.Instance,
                    LogLevel = LogSeverity.Verbose,

                });

                client.Log += Log;
                client.JoinedGuild += JoinedGuild;
                client.GuildAvailable += JoinedGuild;
                client.LeftGuild += LeftGuild;

                prefix = cfgjson.CommandPrefix;

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Registering Commands and Modules..."));
                commands = new CommandService(new CommandServiceConfig()
                {
                    CaseSensitiveCommands = false
                });

                string token = cfgjson.Token; // Remember to keep this private!

                await InstallCommands();

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Logging In..."));
                await client.LoginAsync(TokenType.Bot, token);
                await client.StartAsync();

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Logged In."));

                await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶");
                await client.SetStatusAsync(UserStatus.DoNotDisturb);
                AppDomain.CurrentDomain.UnhandledException += CatchFatalException;

                // Block this task until the program is closed.
                await Task.Delay(-1);
            }
            catch (Exception e)
            {
                await GlobalVars.Log(new LogMessage(LogSeverity.Critical, Process.GetCurrentProcess().ProcessName, "Failed to launch.", e));
            }
        }

        #endregion

        #region Program Events

        private Task Log(LogMessage arg)
        {
            GlobalVars.Log(arg);

            return Task.CompletedTask;
        }

        public async Task LeftGuild(SocketGuild arg)
        {
            await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Left guild " + arg.Name));

            //DBase.RemoveServerEntry(arg.Id.ToString());

            await Task.CompletedTask;
        }

        public async Task JoinedGuild(SocketGuild arg)
        {
            await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Joined guild " + arg.Name));

            if (!await guildService.GuildExistsAsync(arg))
            {
                await guildService.AddGuildAsync(arg);
            }

            await Task.CompletedTask;
        }

        public async Task InstallCommands()
        {
            map.AddSingleton(new ReliabilityService(client, Log));
            map.AddSingleton(new TimerService(client));
            services = map.BuildServiceProvider();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);

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
            if (Directory.Exists(GlobalVars.CacheFolder))
            {
                DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                GlobalVars.ClearReadOnly(users);
            }

            await HandleCommand(messageParam);
        }

        public async Task HandleCommand_MessageEdited(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            if (Directory.Exists(GlobalVars.CacheFolder))
            {
                DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                GlobalVars.ClearReadOnly(users);
            }

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

            GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Status of bot changed from " + status_before + " to " + status_after));

            return Task.CompletedTask;
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            SocketUserMessage message = messageParam as SocketUserMessage;
            if (message == null)
            {
                return;
            }

            // Create a Command Context
            CommandContext context = new CommandContext(client, message);
            SocketUser curUser = context.Client.CurrentUser as SocketUser;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            if (message.HasMentionPrefix(curUser, ref argPos) && context.Message.Content.Contains("dm me"))
            {
                string[] hellostrings = { "Hello!", "Hello World!", "Yo!", "OK!", "Hi!", "...", "Salutations!", "Hola!", "こんにちは！", "你好！", "Здравствуйте!", "Bonjour!", "Hallo!", "Ciao!", "Hej!", "여보세요!", "Koa!", "Aloha!", "مرحبا!" };

                string replystring = hellostrings[new Random().Next(0, hellostrings.Length - 1)];

                if (!GlobalVars.GetDMBool())
                {
                    await context.User.SendMessageAsync(replystring);
                }

                return;
            }

            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!message.HasCharPrefix(Convert.ToChar(prefix), ref argPos) || message.IsPinned || (message.Author.IsBot && message.Author != curUser))
            {
                return;
            }

            string convertedMessage = message.Content.Replace(prefix, "");
            string[] words = convertedMessage.Split(' ');
            List<string> wordlist = words.OfType<string>().ToList();
            bool wordinlist = commandList.Intersect(wordlist).Any();

            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            if (wordinlist == true)
            {
                Task typing = context.Channel.TriggerTypingAsync();

                SocketGuildUser DiscordCaller = message.Author as SocketGuildUser;

                if (DiscordCaller != null)
                {
                    SocketGuild guild = DiscordCaller.Guild;
                    string callerserverid = guild.Id.ToString();
                    bool isonblacklist = await userService.GetBlacklistedAsync(message.Author);

                    if (isonblacklist == true)
                    {
                        await context.Channel.SendMessageAsync("You have been blacklisted from fmbot. Please contact an FMBot administrator if you have any questions regarding this decision.");
                        return;
                    }

                    if (CooldownUser.IncomingRequest(client, DiscordCaller.Id) != false)
                    {
                        IResult result = await commands.ExecuteAsync(context, argPos, services);
                        if (!result.IsSuccess)
                        {
                            await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, result.Error + ": " + result.ErrorReason));
                        }
                        else
                        {
                            GlobalVars.CommandExecutions += 1;
                            GlobalVars.CommandExecutions_Servers += 1;
                        }
                    }
                }
                else
                {
                    if (CooldownUser.IncomingRequest(client, message.Author.Id) != false)
                    {
                        IResult result = await commands.ExecuteAsync(context, argPos, services);
                        if (!result.IsSuccess)
                        {
                            await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, result.Error + ": " + result.ErrorReason));
                        }
                        else
                        {
                            GlobalVars.CommandExecutions += 1;
                            GlobalVars.CommandExecutions_DMs += 1;
                        }
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
            try
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();
                LastfmClient fmclient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                string LastFMName = "lastfmsupport";
                if (!LastFMName.Equals("NULL"))
                {
                    IF.Lastfm.Core.Api.Helpers.PageResponse<IF.Lastfm.Core.Objects.LastTrack> tracks = await fmclient.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                    if (tracks.Any())
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Last.FM API is online"));
                    }
                    else
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, "Last.FM API is offline, rebooting..."));
                        Environment.Exit(1);
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, "Bypassing API requirement..."));
                //continue if we don't have anything
            }
        }

        #endregion
    }
}
