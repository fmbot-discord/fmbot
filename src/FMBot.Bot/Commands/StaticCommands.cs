using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class StaticCommands : ModuleBase
    {
        private readonly CommandService _service;

        private readonly UserService userService = new UserService();

        private readonly GuildService guildService = new GuildService();

        public StaticCommands(CommandService service)
        {
            _service = service;
        }


        [Command("fminvite"), Summary("Invites the bot to a server")]
        [Alias("fmserver")]
        public async Task inviteAsync()
        {
            EmbedBuilder builder = new EmbedBuilder();

            string SelfID = Context.Client.CurrentUser.Id.ToString();

            builder.AddField("Invite the bot to your own server with the link below:",
                "https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=50176");

            builder.AddField("Join the FMBot server for support and updates:",
                "https://discord.gg/srmpCaa");


            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }

        [Command("fmdonate"), Summary("Please donate if you like this bot!")]
        public async Task donateAsync()
        {
            await ReplyAsync("If you like the bot and you would like to support its development, feel free to support the developer at: https://www.paypal.me/Bitl");
        }

        [Command("fmgithub"), Summary("GitHub Page")]
        public async Task githubAsync()
        {
            await ReplyAsync("https://github.com/Bitl/FMBot_Discord");
        }

        [Command("fmgitlab"), Summary("GitLab Page")]
        public async Task gitlabAsync()
        {
            await ReplyAsync("https://gitlab.com/Bitl/FMBot_Discord");
        }

        [Command("fmbugs"), Summary("Report bugs here!")]
        public async Task bugsAsync()
        {
            await ReplyAsync("Report bugs here:\nGithub: https://github.com/Bitl/FMBot_Discord/issues \nGitLab: https://gitlab.com/Bitl/FMBot_Discord/issues");
        }

        [Command("fmstatus"), Summary("Displays bot stats.")]
        public async Task statusAsync()
        {
            ISelfUser SelfUser = Context.Client.CurrentUser;

            EmbedAuthorBuilder eab = new EmbedAuthorBuilder
            {
                IconUrl = SelfUser.GetAvatarUrl(),
                Name = SelfUser.Username
            };

            EmbedBuilder builder = new EmbedBuilder();
            builder.WithAuthor(eab);
            builder.WithDescription(SelfUser.Username + " Statistics");

            TimeSpan startTime = (DateTime.Now - Process.GetCurrentProcess().StartTime);

            DiscordShardedClient client = Context.Client as DiscordShardedClient;

            SocketSelfUser SocketSelf = Context.Client.CurrentUser as SocketSelfUser;

            string status = "Online";

            switch (SocketSelf.Status)
            {
                case UserStatus.Offline: status = "Offline"; break;
                case UserStatus.Online: status = "Online"; break;
                case UserStatus.Idle: status = "Idle"; break;
                case UserStatus.AFK: status = "AFK"; break;
                case UserStatus.DoNotDisturb: status = "Do Not Disturb"; break;
                case UserStatus.Invisible: status = "Invisible/Offline"; break;
            }

            string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            int fixedCmdGlobalCount = GlobalVars.CommandExecutions + 1;
            int fixedCmdGlobalCount_Servers = GlobalVars.CommandExecutions_Servers + 1;
            int fixedCmdGlobalCount_DMs = GlobalVars.CommandExecutions_DMs + 1;

            builder.AddField("Bot Uptime: ", startTime.ToReadableString(), true);
            builder.AddField("Server Uptime: ", GlobalVars.SystemUpTime().ToReadableString(), true);
            builder.AddField("Usercount: ", (await userService.GetUserCountAsync()).ToString(), true);
            builder.AddField("Servercount: ", client.Guilds.Count, true);
            builder.AddField("Commands used since bot start: ", fixedCmdGlobalCount);
            builder.AddField("Commands in servers: ", fixedCmdGlobalCount_Servers);
            builder.AddField("Commands in DMs ", fixedCmdGlobalCount_DMs);
            builder.AddField("Bot status: ", status, true);
            builder.AddField("Latency: ", client.Latency + "ms", true);
            builder.AddField("Shards: ", client.Shards.Count, true);
            builder.AddField("Bot version: ", assemblyVersion, true);

            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }


        [Command("fmhelp"), Summary("Quick help summary to get started.")]
        [Alias("fmbot")]
        public async Task fmhelpAsync()
        {
            string prefix = ConfigData.Data.CommandPrefix;

            EmbedBuilder builder = new EmbedBuilder
            {
                Title = prefix + "FMBot Quick Start Guide",
            };

            builder.AddField(prefix + "fm 'lastfm username/ discord user'",
                "Displays your stats, or stats of entered username or user");

            builder.AddField(prefix + "fmset 'username' 'embedmini/embedfull/textmini/textfull'",
                "Sets your default LastFM name, followed by the display mode you want to use");

            builder.AddField(prefix + "fmrecent 'lastfm username/ discord user' '1-10'",
                "Shows a list of your most recent tracks, defaults to 5");

            builder.AddField(prefix + "fmrecent 'lastfm username/ discord user' '1-10'",
                "Shows a list of your most recent tracks, defaults to 5");

            builder.AddField(prefix + "fmchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'titles/notitles'",
                "Generates an image chart of your top albums");

            builder.AddField(prefix + "fmspotify",
                "Gets the spotify link of your last played song");

            builder.AddField(prefix + "fmyoutube",
                "Gets the youtube link of your last played song");

            builder.AddField(prefix + "fmfriends",
                "Get a list of the songs your friends played");

            builder.WithFooter("Please use `" + prefix + "fmfullhelp` to get a list of all possible commands.");

            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }


        [Command("fmfullhelp"), Summary("Displays this list.")]
        public async Task fmfullhelpAsync()
        {
            string prefix = ConfigData.Data.CommandPrefix;

            ISelfUser SelfUser = Context.Client.CurrentUser;

            string description = null;
            int length = 0;

            EmbedBuilder builder = new EmbedBuilder();

            foreach (ModuleInfo module in _service.Modules.OrderByDescending(o => o.Commands.Count()).Where(w => !w.Name.Contains("SecretCommands") && !w.Name.Contains("OwnerCommands") && !w.Name.Contains("AdminCommands") && !w.Name.Contains("GuildCommands")))
            {
                foreach (CommandInfo cmd in module.Commands)
                {
                    PreconditionResult result = await cmd.CheckPreconditionsAsync(Context);
                    if (result.IsSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(cmd.Summary))
                        {
                            description += $"{prefix}{cmd.Aliases.First()} - {cmd.Summary}\n";
                        }
                        else
                        {
                            description += $"{prefix}{cmd.Aliases.First()}\n";
                        }
                    }
                }


                if (description.Length < 1024)
                {
                    builder.AddField
                        (module.Name + (module.Summary != null ? " - " + module.Summary : ""),
                        description != null ? description : "");
                }


                length += description.Length;
                description = null;

                if (length < 1990)
                {
                    await Context.User.SendMessageAsync("", false, builder.Build());

                    builder = new EmbedBuilder();
                    length = 0;
                }
            }


            builder = new EmbedBuilder
            {
                Title = "Additional information",
            };

            builder.AddField("Quick tips",
                "- Be sure to use 'help' after a command name to see the parameters. \n" +
                "- Chart sizes range from 3x3 to 10x10 \n" +
                "- Most commands have no required parameters");


            builder.AddField("Setting your username",
                "Use `" + prefix + "fmset 'username' 'embedfull/embedmini/textfull/textmini'` to set your global LastFM username. " +
                "The last parameter means the mode that your embed will be");


            builder.AddField("Making album charts",
                "`" + prefix + "fmchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'`");


            builder.AddField("Making artist charts",
                "`" + prefix + "fmartistchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'`");


            builder.AddField("Setting the default server settings",
                "Please note that server defaults are a planned feature. \n" +
                "Only users with the 'Ban Members' permission or admins can use this command. \n" +
                "`" + prefix + "fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'`");

            builder.WithFooter("Still need help? Join the FMBot Discord Server: https://discord.gg/srmpCaa");

            await Context.User.SendMessageAsync("", false, builder.Build());

            if (!guildService.CheckIfDM(Context))
            {
                await Context.Channel.SendMessageAsync("Check your DMs!");
            }

        }
    }
}