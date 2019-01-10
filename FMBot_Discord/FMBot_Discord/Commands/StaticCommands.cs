using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot
{
    public class StaticCommands : ModuleBase
    {
        private readonly CommandService commandService = new CommandService();

        private readonly UserService userService = new UserService();

        private readonly GuildService guildService = new GuildService();


        [Command("fminvite"), Summary("Invites the bot to a server")]
        public async Task inviteAsync()
        {
            string SelfID = Context.Client.CurrentUser.Id.ToString();
            await ReplyAsync("https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=0");
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

        [Command("fmserver"), Summary("Join the Discord server!")]
        public async Task serverAsync()
        {
            await ReplyAsync("Join the Discord server! https://discord.gg/srmpCaa");
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

            DiscordSocketClient SocketClient = Context.Client as DiscordSocketClient;
            int SelfGuilds = SocketClient.Guilds.Count();

            int userCount = await userService.GetUserCountAsync();

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

            builder.AddInlineField("Bot Uptime: ", startTime.ToReadableString());
            builder.AddInlineField("Server Uptime: ", GlobalVars.SystemUpTime().ToReadableString());
            builder.AddInlineField("Number of users in the database: ", userCount.ToString());
            builder.AddInlineField("Total number of command executions since bot start: ", fixedCmdGlobalCount);
            builder.AddInlineField("Command executions in servers since bot start: ", fixedCmdGlobalCount_Servers);
            builder.AddInlineField("Command executions in DMs since bot start: ", fixedCmdGlobalCount_DMs);
            builder.AddField("Number of servers the bot is on: ", SelfGuilds);
            builder.AddField("Bot status: ", status);
            builder.AddField("Bot version: ", assemblyVersion);

            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }


        [Command("fmhelp"), Summary("Quick help summary to get started.")]
        [Alias("fmbot")]
        public async Task fmhelpAsync()
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();
            string prefix = cfgjson.CommandPrefix;

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

            builder.WithFooter("To get an extensive list of all possible commands, please use .fmfullhelp (note: broken right now)");

            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }


        [Command("fmfullhelp"), Summary("Displays this list.")]
        public async Task fmfullhelpAsync()
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            string prefix = cfgjson.CommandPrefix;

            ISelfUser SelfUser = Context.Client.CurrentUser;

            foreach (ModuleInfo module in commandService.Modules)
            {
                string description = null;
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

                if (!string.IsNullOrWhiteSpace(description))
                {
                    await Context.User.SendMessageAsync(module.Name + "\n" + description);
                }
            }

            string helpstring = SelfUser.Username + " Info\n\nBe sure to use 'help' after a command name to see the parameters.\n\n" +
                "Chart sizes range from 3x3 to 10x10.\n\nModes for the fmset command:\nembedmini\nembedfull\ntextfull\ntextmini\nuserdefined (fmserverset only)\n\n" +
                "FMBot Time Periods for the fmchart, fmartistchart, fmartists, and fmalbums commands:\nweekly\nweek\nw\nmonthly\nmonth\nm\nyearly\nyear\ny\noverall\nalltime\no\nat\n\n" +
                "FMBot Title options for FMChart:\ntitles\nnotitles";

            if (!guildService.CheckIfDM(Context))
            {
                await Context.Channel.SendMessageAsync("Check your DMs!");
                await Context.User.SendMessageAsync(helpstring);
            }
            else
            {
                await Context.Channel.SendMessageAsync(helpstring);
            }

        }
    }
}
