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

        public static List<DateTimeOffset> stackCooldownTimer = new List<DateTimeOffset>();

        public static List<SocketGuildUser> stackCooldownTarget = new List<SocketGuildUser>();

        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            try
            {
                string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "FMBot v" + assemblyVersion + " loading...")).ConfigureAwait(false);

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync().ConfigureAwait(false);

                if (!Directory.Exists(GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(GlobalVars.CacheFolder);
                }
                else
                {
                    DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                    GlobalVars.ClearReadOnly(users);
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Initalizing Last.FM...")).ConfigureAwait(false);
                await TestLastFMAPI().ConfigureAwait(false);

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Initalizing Discord...")).ConfigureAwait(false);
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

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Registering Commands and Modules...")).ConfigureAwait(false);
                commands = new CommandService(new CommandServiceConfig()
                {
                    CaseSensitiveCommands = false
                });

                string token = cfgjson.Token; // Remember to keep this private!

                await InstallCommands().ConfigureAwait(false);

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Logging In...")).ConfigureAwait(false);
                await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
                await client.StartAsync().ConfigureAwait(false);

                await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Logged In.")).ConfigureAwait(false);

                await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶").ConfigureAwait(false);
                await client.SetStatusAsync(UserStatus.DoNotDisturb).ConfigureAwait(false);
                AppDomain.CurrentDomain.UnhandledException += CatchFatalException;

                // Block this task until the program is closed.
                await Task.Delay(-1).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await GlobalVars.Log(new LogMessage(LogSeverity.Critical, Process.GetCurrentProcess().ProcessName, "Failed to launch.", e)).ConfigureAwait(false);
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
            await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Left guild " + arg.Name)).ConfigureAwait(false);

            //DBase.RemoveServerEntry(arg.Id.ToString());

            await Task.CompletedTask;
        }

        public async Task JoinedGuild(SocketGuild arg)
        {
            await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Joined guild " + arg.Name)).ConfigureAwait(false);

            if (!await guildService.GuildExistsAsync(arg).ConfigureAwait(false))
            {
                await guildService.AddGuildAsync(arg).ConfigureAwait(false);
            }

            await Task.CompletedTask;
        }

        public async Task InstallCommands()
        {
            map.AddSingleton(new ReliabilityService(client, Log));
            map.AddSingleton(new TimerService(client));
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
            if (!(messageParam is SocketUserMessage message))
            {
                return;
            }

            // Create a Command Context
            CommandContext context = new CommandContext(client, message);
            SocketUser curUser = context.Client.CurrentUser as SocketUser;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            if (message.HasMentionPrefix(curUser, ref argPos) && (context.Message.Content.Contains("dm me") || context.Message.Content.Contains("help")))
            {
                string[] hellostrings = { "Hello!", "Hello World!", "Yo!", "OK!", "Hi!", "...", "Salutations!", "Hola!", "こんにちは！", "你好！", "Здравствуйте!", "Bonjour!", "Hallo!", "Ciao!", "Hej!", "여보세요!", "Koa!", "Aloha!", "مرحبا!" };

                string replystring = hellostrings[new Random().Next(0, hellostrings.Length - 1)];

                if (!GlobalVars.GetDMBool())
                {
                    //StaticCommands staticCommands = new StaticCommands(services);
                    //Task help = staticCommands.fmfullhelpAsync();
                    await context.User.SendMessageAsync(replystring).ConfigureAwait(false);
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
                    bool isonblacklist = await userService.GetBlacklistedAsync(message.Author).ConfigureAwait(false);

                    if (isonblacklist)
                    {
                        await context.Channel.SendMessageAsync("You have been blacklisted from fmbot. Please contact an FMBot administrator if you have any questions regarding this decision.").ConfigureAwait(false);
                        return;
                    }

                    if (stackCooldownTarget.Contains(DiscordCaller))
                    {
                        //If they have used this command before, take the time the user last did something, add 5 seconds, and see if it's greater than this very moment.
                        if (stackCooldownTimer[stackCooldownTarget.IndexOf(DiscordCaller)].AddSeconds(5) >= DateTimeOffset.Now)
                        {
                            //If enough time hasn't passed, reply letting them know how much longer they need to wait, and end the code.
                            int secondsLeft = (int)(stackCooldownTimer[stackCooldownTarget.IndexOf(DiscordCaller)].AddSeconds(5) - DateTimeOffset.Now).TotalSeconds;
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
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, result.Error + ": " + result.ErrorReason)).ConfigureAwait(false);
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
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, result.Error + ": " + result.ErrorReason)).ConfigureAwait(false);
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
            try
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync().ConfigureAwait(false);
                LastfmClient fmclient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                const string LastFMName = "lastfmsupport";
                if (!LastFMName.Equals("NULL"))
                {
                    IF.Lastfm.Core.Api.Helpers.PageResponse<IF.Lastfm.Core.Objects.LastTrack> tracks = await fmclient.User.GetRecentScrobbles(LastFMName, null, 1, 2).ConfigureAwait(false);
                    if (tracks.Any())
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Info, Process.GetCurrentProcess().ProcessName, "Last.FM API is online")).ConfigureAwait(false);
                    }
                    else
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, "Last.FM API is offline, rebooting...")).ConfigureAwait(false);
                        Environment.Exit(1);
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, Process.GetCurrentProcess().ProcessName, "Bypassing API requirement...")).ConfigureAwait(false);
                //continue if we don't have anything
            }
        }

        #endregion
    }
}
