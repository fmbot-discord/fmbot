using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;

namespace FMBot.Bot.Commands
{
    [Name("Static commands")]
    public class StaticCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly FriendsService _friendService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly SupporterService _supporterService;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new List<DateTimeOffset>();
        private static readonly List<SocketUser> StackCooldownTarget = new List<SocketUser>();

        public StaticCommands(
                CommandService service,
                FriendsService friendsService,
                GuildService guildService,
                IPrefixService prefixService,
                SupporterService supporterService,
                UserService userService
            )
        {
            this._friendService = friendsService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._service = service;
            this._supporterService = supporterService;
            this._userService = userService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
        }

        [Command("invite", RunMode = RunMode.Async)]
        [Summary("Info for inviting the bot to a server")]
        [Alias("server", "info")]
        public async Task InviteAsync()
        {
            var selfId = this.Context.Client.CurrentUser.Id.ToString();
            var embedDescription = new StringBuilder();

            embedDescription.AppendLine("- You can invite .fmbot to your own server by **[clicking here](" +
                "https://discord.com/oauth2/authorize?" +
                $"client_id={selfId}" +
                "&scope=bot%20applications.commands" +
                $"&permissions={Constants.InviteLinkPermissions}).**");

            embedDescription.AppendLine(
                "- Join the [.fmbot server](http://server.fmbot.xyz/) for support and updates.");

            embedDescription.AppendLine(
                "- Help us cover hosting and other costs on our [OpenCollective](https://opencollective.com/fmbot)");

            embedDescription.AppendLine(
                "- Check our [website](https://fmbot.xyz/) for more information.");

            this._embed.WithDescription(embedDescription.ToString());

            if (IsBotSelfHosted(this.Context.Client.CurrentUser.Id))
            {
                this._embed.AddField("Note:",
                    "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. " +
                    "The invite link linked here invites the self-hosted instance and not the official .fmbot.");
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("donate", RunMode = RunMode.Async)]
        [Summary("Please donate if you like this bot!")]
        [Alias("support")]
        public async Task DonateAsync()
        {
            var embedDescription = new StringBuilder();

            embedDescription.AppendLine(".fmbot is non-commercial and is hosted and maintained by volunteers.");
            embedDescription.AppendLine("You can help us cover hosting and other costs on our [OpenCollective](https://opencollective.com/fmbot).");
            embedDescription.AppendLine();
            embedDescription.AppendLine("We use OpenCollective so we can be transparent about our expenses. If you decide to sponsor us, you can see exactly where your money goes.");
            embedDescription.AppendLine();
            embedDescription.AppendLine("**.fmbot supporter advantages include**:\n" +
                                        "- An emote behind their name (⭐)\n" +
                                        "- Their name added to `.fmsupporters`\n" +
                                        $"- A 1/{Constants.SupporterMessageChance} chance of sponsoring a chart\n" +
                                        "- WhoKnows tracking increased to top 25K (instead of top 4/5/6k artist/albums/tracks)");

            if (IsBotSelfHosted(this.Context.Client.CurrentUser.Id))
            {
                this._embed.AddField("Note:",
                    "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. Any supporter advantages will not apply on this bot.");
            }

            this._embed.WithDescription(embedDescription.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("status", RunMode = RunMode.Async)]
        [Summary("Displays bot stats.")]
        public async Task StatusAsync()
        {
            var selfUser = this.Context.Client.CurrentUser;

            this._embedAuthor.WithIconUrl(selfUser.GetAvatarUrl());
            this._embedAuthor.WithName(selfUser.Username);
            this._embedAuthor.WithUrl("https://fmbot.xyz/");

            this._embed.WithAuthor(this._embedAuthor);

            var currentProcess = Process.GetCurrentProcess();

            var startTime = DateTime.Now - currentProcess.StartTime;
            var currentMemoryUsage = currentProcess.WorkingSet64;
            var peakMemoryUsage = currentProcess.PeakWorkingSet64;

            var client = this.Context.Client as DiscordShardedClient;

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            var ticks = Stopwatch.GetTimestamp();
            var upTime = (double)ticks / Stopwatch.Frequency;
            var upTimeInSeconds = TimeSpan.FromSeconds(upTime);

            var description = "";
            description += $"**Bot Uptime:** `{startTime.ToReadableString()}`\n";
            description += $"**Server Uptime:** `{upTimeInSeconds.ToReadableString()}`\n";
            description += $"**Usercount:** `{(await this._userService.GetTotalUserCountAsync()).ToString()}`  (Discord: `{client.Guilds.Select(s => s.MemberCount).Sum()}`)\n";
            description += $"**Friendcount:** `{await this._friendService.GetTotalFriendCountAsync()}`\n";
            description += $"**Servercount:** `{client.Guilds.Count}`  (Shards: `{client.Shards.Count}`)\n";
            description += $"**Commands used:** `{Statistics.CommandsExecuted.Value}`\n";
            description += $"**Last.fm API calls:** `{Statistics.LastfmApiCalls.Value}`  (Ex. authorized: `{Statistics.LastfmAuthorizedApiCalls.Value}`)\n";
            description += $"**Memory usage:** `{currentMemoryUsage.ToFormattedByteString()}`  (Peak: `{peakMemoryUsage.ToFormattedByteString()}`)\n";
            description += $"**Average shard latency:** `{Math.Round(client.Shards.Select(s => s.Latency).Average(), 2) + "ms`"}\n";
            description += $"**Bot version:** `{assemblyVersion}`\n";
            description += $"**Self-hosted:** `{IsBotSelfHosted(this.Context.Client.CurrentUser.Id).ToString()}`\n";

            this._embed.WithDescription(description);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("help", RunMode = RunMode.Async)]
        [Summary("Quick help summary to get started.")]
        [Alias("bot")]
        public async Task HelpAsync()
        {
            var customPrefix = true;
            var prefix = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            if (prefix == null)
            {
                prefix = ConfigData.Data.Bot.Prefix;
                customPrefix = false;
            }

            this._embed.WithTitle(".fmbot Quick Start Guide");

            var mainCommand = "fm";
            if (!customPrefix)
            {
                mainCommand = "";
            }

            this._embed.AddField($"Main command `{prefix}{mainCommand}`",
                "Displays last scrobbles, and looks different depending on the mode you've set.");

            this._embed.AddField($"Connecting your Last.fm account: `{prefix}login`",
                $"Not receiving a DM from .fmbot when logging in? Please check if you have DMs enabled in this servers privacy settings.\n" +
                $"For changing how your .fm command looks use `{prefix}mode`.");

            if (customPrefix)
            {
                this._embed.AddField("Custom prefix:",
                    $"This server has the `{prefix}` prefix.\n" +
                    $"Note that the documentation has the `.fm` prefix everywhere, so you'll have to replace `.fm` with `{prefix}`.");
            }

            this._embed.AddField("For more commands and info, please read the documentation here:",
                "https://fmbot.xyz/commands/");

            if (IsBotSelfHosted(this.Context.Client.CurrentUser.Id))
            {
                this._embed.AddField("Note:",
                    "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. \n" +
                    "Keep in mind that the instance might not be fully up to date or other users might not be registered.");
            }

            if (PublicProperties.IssuesAtLastFm)
            {
                this._embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }


        [Command("supporters", RunMode = RunMode.Async)]
        [Summary("Displays all .fmbot supporters.")]
        [Alias("donators", "donors", "backers")]
        public async Task AllSupportersAsync()
        {
            this._embed.WithTitle(".fmbot supporters");

            var supporters = await this._supporterService.GetAllVisibleSupporters();

            var description = new StringBuilder();

            foreach (var supporter in supporters)
            {
                var type = supporter.SupporterType switch
                {
                    SupporterType.Guild => " (server)",
                    SupporterType.User => "",
                    SupporterType.Company => " (business)",
                    _ => ""
                };

                description.AppendLine($" - **{supporter.Name}** {type}");
            }

            description.AppendLine();
            description.AppendLine("Thank you to all our supporters that help keep .fmbot running. If you would like to be on this list too, please check out our [OpenCollective](https://opencollective.com/fmbot). \n" +
                                   "For all information on donating to .fmbot you can check out `.fmdonate`.");

            this._embed.WithDescription(description.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }


        [Command("countdown", RunMode = RunMode.Async)]
        [Summary("Counts down")]
        public async Task CountdownAsync(int countdown = 3)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            if (countdown > 5)
            {
                countdown = 5;
            }

            if (countdown < 1)
            {
                countdown = 1;
            }

            var msg = this.Context.Message as SocketUserMessage;
            if (StackCooldownTarget.Contains(this.Context.Message.Author))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(countdown + 30) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                        .AddSeconds(countdown + 30) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= 20)
                    {
                        var secondString = secondsLeft == 1 ? "second" : "seconds";
                        await ReplyAsync($"Please wait {secondsLeft} {secondString} before starting another countdown.");
                        this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    }

                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
            }
            else
            {
                StackCooldownTarget.Add(msg.Author);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }

            await ReplyAsync($"Countdown for `{countdown}` seconds starting!");
            await Task.Delay(4000);

            for (var i = countdown; i > 0; i--)
            {
                _ = ReplyAsync(i.ToString());
                await Task.Delay(1000);
            }

            await ReplyAsync("Go!");
            this.Context.LogCommandUsed();
        }


        [Command("fullhelp", RunMode = RunMode.Async)]
        [Summary("Displays this list.")]
        public async Task FullHelpAsync()
        {
            var prefix = ConfigData.Data.Bot.Prefix;

            var embed = new EmbedBuilder();

            foreach (var module in this._service.Modules
                .OrderByDescending(o => o.Commands.Count).Where(w =>
                !w.Attributes.OfType<ExcludeFromHelp>().Any()))
            {
                var moduleCommands = "";
                foreach (var cmd in module.Commands)
                {
                    var result = await cmd.CheckPreconditionsAsync(this.Context);
                    if (result.IsSuccess)
                    {
                        if (!string.IsNullOrEmpty(moduleCommands))
                        {
                            moduleCommands += ", ";
                        }

                        moduleCommands += $"`{prefix}{cmd.Name}`";
                    }
                }

                var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $" - {module.Summary}";

                if (!string.IsNullOrEmpty(module.Name) && !string.IsNullOrEmpty(moduleCommands))
                {
                    embed.AddField(
                        module.Name + moduleSummary,
                        moduleCommands,
                        true);
                }

            }

            embed.WithFooter("Add 'help' after a command to get more info. For example: .fmchart help");
            await this.Context.Channel.SendMessageAsync("", false, embed.Build());

            this.Context.LogCommandUsed();
        }

        private static bool IsBotSelfHosted(ulong botId)
        {
            return !botId.Equals(Constants.BotProductionId) && !botId.Equals(Constants.BotDevelopId);
        }
    }
}
