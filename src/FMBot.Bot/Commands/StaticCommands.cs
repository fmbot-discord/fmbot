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
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class StaticCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly GuildService _guildService = new GuildService();

        private readonly CommandService _service;
        private readonly UserService _userService = new UserService();

        public StaticCommands(CommandService service)
        {
            this._service = service;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
        }


        [Command("fminvite")]
        [Summary("Info for inviting the bot to a server")]
        [Alias("fmserver")]
        public async Task inviteAsync()
        {
            var SelfID = this.Context.Client.CurrentUser.Id.ToString();

            this._embed.AddField("Invite the bot to your own server with the link below:",
                "https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=" +
                Constants.InviteLinkPermissions);

            this._embed.AddField("Join the FMBot server for support and updates:",
                "https://discord.gg/srmpCaa");

            this._embed.AddField("Please upvote us on Discord Bots if you enjoy the bot:",
                "https://discordbots.org/bot/356268235697553409");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        }

        [Command("fminfo")]
        [Summary("Please donate if you like this bot!")]
        [Alias("fmdonate", "fmgithub", "fmgitlab", "fmissues", "fmbugs")]
        public async Task InfoAsync()
        {
            var SelfID = this.Context.Client.CurrentUser.Id.ToString();

            this._embed.AddField("Invite the bot to your own server with the link below:",
                "https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=" +
                Constants.InviteLinkPermissions);

            this._embed.AddField("Like the bot and want support its development?",
                "Feel free to support the developers here: \n" +
                "https://www.paypal.me/Bitl and https://www.paypal.me/th0m");

            this._embed.AddField("Post issues and feature requests here:",
                "https://github.com/fmbot-discord/fmbot/issues/new/choose");

            this._embed.AddField("View the code on Github:",
                "https://github.com/fmbot-discord/fmbot");

            this._embed.AddField("Or on GitLab:",
                "https://gitlab.com/Bitl/FMBot_Discord");

            this._embed.AddField("Join the FMBot server for support and updates:",
                "https://discord.gg/srmpCaa");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        }

        [Command("fmstatus")]
        [Summary("Displays bot stats.")]
        public async Task statusAsync()
        {
            var selfUser = this.Context.Client.CurrentUser;

            this._embedAuthor.WithIconUrl(selfUser.GetAvatarUrl());
            this._embedAuthor.WithName(selfUser.Username);

            this._embed.WithAuthor(this._embedAuthor);

            var startTime = DateTime.Now - Process.GetCurrentProcess().StartTime;

            var client = this.Context.Client as DiscordShardedClient;

            var socketSelfUser = this.Context.Client.CurrentUser as SocketSelfUser;

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            var fixedCmdGlobalCount = GlobalVars.CommandExecutions + 1;

            this._embed.AddField("Bot Uptime: ", startTime.ToReadableString(), true);
            this._embed.AddField("Server Uptime: ", GlobalVars.SystemUpTime().ToReadableString(), true);
            this._embed.AddField("Usercount: ", (await this._userService.GetUserCountAsync()).ToString(), true);
            this._embed.AddField("Discord usercount: ", client.Guilds.Select(s => s.MemberCount).Sum(), true);
            this._embed.AddField("Servercount: ", client.Guilds.Count, true);
            this._embed.AddField("Commands used: ", fixedCmdGlobalCount, true);
            this._embed.AddField("Last.FM API calls: ", GlobalVars.LastFMApiCalls, true);
            this._embed.AddField("Bot status: ", socketSelfUser.Status.ToString(), true);
            this._embed.AddField("Average latency: ", client.Shards.Select(s => s.Latency).Average() + "ms", true);
            this._embed.AddField("Shards: ", client.Shards.Count, true);
            this._embed.AddField("Bot version: ", assemblyVersion, true);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        }


        [Command("fmhelp")]
        [Summary("Quick help summary to get started.")]
        [Alias("fmbot")]
        public async Task fmhelpAsync()
        {
            var prefix = ConfigData.Data.CommandPrefix;

            this._embed.WithTitle(prefix + "FMBot Quick Start Guide");

            this._embed.AddField(prefix + "fm 'lastfm username/ discord user'",
                "Displays your stats, or stats of entered username or user");

            this._embed.AddField(prefix + "fmset 'username' 'embedmini/embedfull/textmini/textfull'",
                "Sets your default LastFM name, followed by the display mode you want to use");

            this._embed.AddField(prefix + "fmrecent 'lastfm username/ discord user' '1-10'",
                "Shows a list of your most recent tracks, defaults to 5");

            this._embed.AddField(prefix + "fmrecent 'lastfm username/ discord user' '1-10'",
                "Shows a list of your most recent tracks, defaults to 5");

            this._embed.AddField(prefix + "fmchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'titles/notitles'",
                "Generates an image chart of your top albums");

            this._embed.AddField(prefix + "fmspotify",
                "Gets the spotify link of your last played song");

            this._embed.AddField(prefix + "fmyoutube",
                "Gets the youtube link of your last played song");

            this._embed.AddField(prefix + "fmfriends",
                "Get a list of the songs your friends played");

            this._embed.WithFooter("Please use `" + prefix + "fmfullhelp` to get a list of all possible commands.");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        }


        [Command("fmfullhelp")]
        [Summary("Displays this list.")]
        public async Task fmfullhelpAsync()
        {
            var prefix = ConfigData.Data.CommandPrefix;

            var SelfUser = this.Context.Client.CurrentUser;

            string description = null;
            var length = 0;

            var builder = new EmbedBuilder();

            foreach (var module in this._service.Modules.OrderByDescending(o => o.Commands.Count()).Where(w =>
                !w.Name.Contains("SecretCommands") && !w.Name.Contains("OwnerCommands") &&
                !w.Name.Contains("AdminCommands") && !w.Name.Contains("GuildCommands")))
            {
                foreach (var cmd in module.Commands)
                {
                    var result = await cmd.CheckPreconditionsAsync(this.Context);
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
                    await this.Context.User.SendMessageAsync("", false, builder.Build());

                    builder = new EmbedBuilder();
                    length = 0;
                }
            }


            builder = new EmbedBuilder
            {
                Title = "Additional information"
            };

            builder.AddField("Quick tips",
                "- Be sure to use 'help' after a command name to see the parameters. \n" +
                "- Chart sizes range from 3x3 to 10x10 \n" +
                "- Most commands have no required parameters");


            builder.AddField("Setting your username",
                "Use `" + prefix +
                "fmset 'username' 'embedfull/embedmini/textfull/textmini'` to set your global LastFM username. " +
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

            await this.Context.User.SendMessageAsync("", false, builder.Build());

            if (!this._guildService.CheckIfDM(this.Context))
            {
                await this.Context.Channel.SendMessageAsync("Check your DMs!");
            }
        }
    }
}
