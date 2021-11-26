using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands
{
    [Name("Static commands")]
    public class StaticCommands : BaseCommandModule
    {
        private readonly CommandService _service;
        private readonly FriendsService _friendService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly SupporterService _supporterService;
        private readonly UserService _userService;
        private InteractiveService Interactivity { get; }

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public StaticCommands(
                CommandService service,
                FriendsService friendsService,
                GuildService guildService,
                IPrefixService prefixService,
                SupporterService supporterService,
                UserService userService,
                IOptions<BotSettings> botSettings,
                InteractiveService interactivity) : base(botSettings)
        {
            this._friendService = friendsService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._service = service;
            this._supporterService = supporterService;
            this._userService = userService;
            this.Interactivity = interactivity;
        }

        [Command("invite", RunMode = RunMode.Async)]
        [Summary("Info for inviting the bot to a server")]
        [Alias("server", "info")]
        [CommandCategories(CommandCategory.Other)]
        public async Task InviteAsync()
        {
            var socketCommandContext = (SocketCommandContext)this.Context;
            var selfId = socketCommandContext.Client.CurrentUser.Id.ToString();
            var embedDescription = new StringBuilder();
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

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

            if (IsBotSelfHosted(socketCommandContext.Client.CurrentUser.Id))
            {
                this._embed.AddField("Note:",
                    "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. " +
                    "The invite link linked here invites the self-hosted instance and not the official .fmbot.");
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("source", RunMode = RunMode.Async)]
        [Summary("Info for inviting the bot to a server")]
        [Alias("github", "gitlab", "opensource", "sourcecode", "code")]
        [CommandCategories(CommandCategory.Other)]
        public async Task SourceAsync()
        {
            var embedDescription = new StringBuilder();
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            embedDescription.AppendLine(".fmbot is open-source, non-profit and maintained by volunteers.");
            embedDescription.AppendLine("The bot is written in C#, uses .NET 6 and Discord.Net Labs.");

            this._embed.WithDescription(embedDescription.ToString());

            this._embed.AddField("Links",
                "[Main repository](https://github.com/fmbot-discord/fmbot/)\n" +
                "[Docs repository](https://github.com/fmbot-discord/docs)\n" +
                "[File an issue](https://github.com/fmbot-discord/fmbot/issues/new/choose)\n" +
                "[Development instructions](https://fmbot.xyz/setup.html)\n" +
                "[OpenCollective](https://opencollective.com/fmbot)");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("outofsync", RunMode = RunMode.Async)]
        [Summary("Info for what to do when now playing track is lagging behind")]
        [Alias("broken", "sync", "fix", "lagging", "stuck")]
        [CommandCategories(CommandCategory.Other)]
        public async Task OutOfSyncAsync()
        {
            var embedDescription = new StringBuilder();
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            this._embed.WithTitle("Using Spotify and tracking is out of sync?");

            embedDescription.AppendLine(".fmbot uses your Last.fm account for knowing what you listen to. ");
            embedDescription.AppendLine($"Unfortunately, Last.fm and Spotify sometimes have issues keeping up to date with your current song, which can cause `{prfx}fm` and other commands to lag behind the song you're currently listening to.");
            embedDescription.AppendLine();
            embedDescription.Append("First, **.fmbot is not affiliated with Last.fm**. Your music is tracked by Last.fm, and not by .fmbot. ");
            embedDescription.AppendLine("This means that this is a Last.fm issue and **not an .fmbot issue**. We can't fix it for you, but we can give you some tips that worked for others.");
            embedDescription.AppendLine();
            embedDescription.AppendLine("Some things you can try that usually work:");
            embedDescription.AppendLine(" - Restarting your Spotify application");
            embedDescription.AppendLine(" - Disconnecting and **reconnecting Spotify in [your Last.fm settings](https://www.last.fm/settings/applications)**");
            embedDescription.AppendLine();
            embedDescription.AppendLine("If the two options above don't work, check out **[the complete guide for this issue on the Last.fm support forums](https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184)**.");

            this._embed.WithDescription(embedDescription.ToString());

            var components = new ComponentBuilder()
                .WithButton("Last.fm settings", style: ButtonStyle.Link, url: "https://www.last.fm/settings/applications")
                .WithButton("Full guide", style: ButtonStyle.Link, url: "https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build(), component: components.Build());
            this.Context.LogCommandUsed();
        }

        [Command("donate", RunMode = RunMode.Async)]
        [Summary("Please donate if you like this bot!")]
        [Alias("support", "patreon", "opencollective", "donations", "support")]
        [CommandCategories(CommandCategory.Other)]
        public async Task DonateAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            var embedDescription = new StringBuilder();

            embedDescription.AppendLine(".fmbot is non-commercial and is hosted and maintained by volunteers.");
            embedDescription.AppendLine("You can help us cover hosting and other costs on our [OpenCollective](https://opencollective.com/fmbot).");
            embedDescription.AppendLine();
            embedDescription.AppendLine("We use OpenCollective so we can be transparent about our expenses. If you decide to sponsor us, you can see exactly where your money goes.");
            embedDescription.AppendLine();
            embedDescription.AppendLine($"Use `{prfx}supporters` to see everyone who has supported us so far!");
            embedDescription.AppendLine();
            embedDescription.AppendLine("**.fmbot supporter advantages include**:\n" +
                                        "- An emote behind their name (⭐)\n" +
                                        "- Their name added to the list of supporters\n" +
                                        "- A chance of sponsoring a chart\n" +
                                        "- Friend limit increased to 15 (up from 12)\n" +
                                        "- WhoKnows tracking increased to all your music (instead of top 4/5/6k artist/albums/tracks)");

            var socketCommandContext = (SocketCommandContext)this.Context;
            if (IsBotSelfHosted(socketCommandContext.Client.CurrentUser.Id))
            {
                this._embed.AddField("Note:",
                    "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. Any supporter advantages will not apply on this bot.");
            }

            this._embed.WithDescription(embedDescription.ToString());

            var components = new ComponentBuilder().WithButton("Get .fmbot supporter", style: ButtonStyle.Link, url: "https://opencollective.com/fmbot/contribute");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build(), component: components.Build());
            this.Context.LogCommandUsed();
        }

        [Command("status", RunMode = RunMode.Async)]
        [Summary("Displays bot stats.")]
        [CommandCategories(CommandCategory.Other)]
        public async Task StatusAsync()
        {
            var socketCommandContext = (SocketCommandContext)this.Context;
            var selfUser = socketCommandContext.Client.CurrentUser;
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

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
            description += $"**Usercount:** `{await this._userService.GetTotalUserCountAsync()}`  (Authorized: `{await this._userService.GetTotalAuthorizedUserCountAsync()}` | Discord: `{client.Guilds.Select(s => s.MemberCount).Sum()}`)\n";
            description += $"**Friendcount:** `{await this._friendService.GetTotalFriendCountAsync()}`\n";
            description += $"**Servercount:** `{client.Guilds.Count}`  (Shards: `{client.Shards.Count}` (`{client.GetShardIdFor(this.Context.Guild)}`))\n";
            description += $"**Commands used:** `{Statistics.CommandsExecuted.Value}`\n";
            description += $"**Last.fm API calls:** `{Statistics.LastfmApiCalls.Value}`  (Ex. authorized: `{Statistics.LastfmAuthorizedApiCalls.Value}`)\n";
            description += $"**MusicBrainz API calls:** `{Statistics.MusicBrainzApiCalls.Value}`\n";
            description += $"**Botscrobbles:** `{Statistics.LastfmScrobbles.Value}`  (Now playing updates: `{Statistics.LastfmNowPlayingUpdates.Value}`)\n";
            description += $"**Memory usage:** `{currentMemoryUsage.ToFormattedByteString()}`  (Peak: `{peakMemoryUsage.ToFormattedByteString()}`)\n";
            description += $"**Average shard latency:** `{Math.Round(client.Shards.Select(s => s.Latency).Average(), 2) + "ms`"}\n";
            //description += $"**Bot version:** `{assemblyVersion}`\n";
            //description += $"**Self-hosted:** `{IsBotSelfHosted(this.Context.Client.CurrentUser.Id).ToString()}`\n";

            this._embed.WithDescription(description);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("shards", RunMode = RunMode.Async)]
        [Summary("Displays bot sharding info.")]
        [ExcludeFromHelp]
        public async Task ShardsAsync()
        {
            this._embed.WithTitle("Bot instance shards");

            var client = this.Context.Client as DiscordShardedClient;

            var onlineShards = new StringBuilder();
            foreach (var shard in client.Shards.Where(w => w.Latency > 0))
            {
                onlineShards.Append($"{shard.ShardId} ({shard.Latency}), ");
            }
            this._embed.AddField("Online shards", onlineShards.Length > 0 ? onlineShards.ToString() : "No online shards");

            var offlineShards = new StringBuilder();
            foreach (var shard in client.Shards.Where(w => w.Latency == 0))
            {
                offlineShards.Append($"{shard.ShardId} ({shard.Latency}), ");
            }
            this._embed.AddField("Offline shards", offlineShards.Length > 0 ? offlineShards.ToString() : "No offline shards");

            if (this.Context.Guild != null)
            {
                this._embed.WithFooter(
                    $"Guild {this.Context.Guild.Name} | {this.Context.Guild.Id} is on shard {client.GetShardIdFor(this.Context.Guild)}");
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("shard", RunMode = RunMode.Async)]
        [Summary("Displays shard info for a specific guild")]
        [GuildOnly]
        [ExcludeFromHelp]
        [Alias("shardinfo")]
        public async Task ShardInfoAsync(ulong? guildId = null)
        {
            if (!guildId.HasValue)
            {
                await this.Context.Channel.SendMessageAsync($"Enter a server id please (this server is `{this.Context.Guild.Id}`)");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var client = this.Context.Client as DiscordShardedClient;

            var guild = client.GetGuild(guildId.Value);

            if (guild != null)
            {
                var shard = client.GetShardFor(guild);


                this._embed.WithDescription($"Guild `{guildId}` is on the following shard:\n\n" +
                                            $"Shard id: `{shard.ShardId}`\n" +
                                            $"Latency: `{shard.Latency}ms`\n" +
                                            $"Guilds: `{shard.Guilds.Count}`\n" +
                                            $"Connection state: `{shard.ConnectionState}`");

                this._embed.WithFooter($"{guild.Name} - {guild.MemberCount} members");
            }
            else
            {
                await this.Context.Channel.SendMessageAsync("Server could not be found. \n" +
                                                            "This either means the bot is not connected to that server or that the bot is not in this server.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("help", RunMode = RunMode.Async)]
        [Summary("Quick help summary to get started.")]
        [Alias("bot", "fmbot")]
        [CommandCategories(CommandCategory.Other)]
        public async Task HelpAsync([Remainder] string extraValues = null)
        {
            var prefix = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            if (!string.IsNullOrWhiteSpace(extraValues))
            {
                if (extraValues.Length > prefix.Length && extraValues.Contains(prefix))
                {
                    extraValues = extraValues.Replace(prefix, "");
                }

                var searchResult = this._service.Search(extraValues);
                if (searchResult.IsSuccess && searchResult.Commands != null && searchResult.Commands.Any())
                {
                    var userName = (this.Context.Message.Author as SocketGuildUser)?.Nickname ?? this.Context.User.Username;
                    this._embed.HelpResponse(searchResult.Commands[0].Command, prefix, userName);
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.Help);
                    return;
                }
            }

            try
            {
                IUserMessage message = null;
                InteractiveMessageResult<MultiSelectionOption<string>> selectedResult = null;

                this._embed.WithColor(DiscordConstants.InformationColorBlue);

                var options = new List<MultiSelectionOption<string>>();
                foreach (var commandCategory in (CommandCategory[])Enum.GetValues(typeof(CommandCategory)))
                {
                    var description = StringExtensions.CommandCategoryToString(commandCategory);
                    options.Add(new MultiSelectionOption<string>(commandCategory.ToString(), commandCategory.ToString(), 1, description?.ToLower() != commandCategory.ToString().ToLower() ? description : null));
                    options.First(x => x.Option == CommandCategory.General.ToString()).IsDefault = true;
                }

                while (selectedResult is null || selectedResult.Status == InteractiveStatus.Success)
                {
                    var commands = "**Commands:** \n";
                    var selectedCategoryOrCommand = selectedResult?.Value?.Value;

                    await SetGeneralHelpEmbed(prefix);

                    if (selectedResult?.Value == null || selectedResult.Value.Row == 1)
                    {
                        options = options.Where(w => w.Row == 1).ToList();
                        if (selectedCategoryOrCommand != null)
                        {
                            Enum.TryParse(selectedCategoryOrCommand, out CommandCategory selectedCategory);

                            var selectedCommands = this._service.Commands.Where(w =>
                                w.Attributes.OfType<CommandCategoriesAttribute>().Select(s => s.Categories).Any(a => a.Contains(selectedCategory))).ToList();

                            Console.WriteLine(selectedCategory);

                            if (selectedCommands.Any())
                            {
                                options.ForEach(x => x.IsDefault = false); // Reset to default
                                options.First(x => x.Option == selectedCategoryOrCommand).IsDefault = true;

                                foreach (var selectedCommand in selectedCommands.Take(25))
                                {
                                    options.Add(new MultiSelectionOption<string>(selectedCommand.Name, selectedCommand.Name, 2, null));
                                }

                                var totalCategories = new List<CommandCategory>();
                                foreach (var selectedCommand in selectedCommands.Select(s => s.Attributes.OfType<CommandCategoriesAttribute>().Select(se => se.Categories)).Distinct())
                                {
                                    foreach (var test in selectedCommand)
                                    {
                                        totalCategories.AddRange(test);
                                    }
                                }

                                var usedCommands = new List<CommandInfo>();

                                foreach (var selectedCommand in selectedCommands.Where(w => w.Attributes.OfType<CommandCategoriesAttribute>().Select(s => s.Categories).Any(a => a.Length == 1 && a.Contains(selectedCategory))))
                                {
                                    if (!usedCommands.Contains(selectedCommand))
                                    {
                                        commands += await CommandInfoToHelpString(prefix, selectedCommand);
                                        usedCommands.Add(selectedCommand);
                                    }
                                }

                                if (selectedCategory != CommandCategory.WhoKnows)
                                {
                                    commands += "\n";

                                    foreach (var selectedCommand in selectedCommands.Where(w => w.Attributes.OfType<CommandCategoriesAttribute>().Select(s => s.Categories).Any(a => a.Contains(CommandCategory.WhoKnows) && a.Length > 1)))
                                    {
                                        if (!usedCommands.Contains(selectedCommand))
                                        {
                                            commands += await CommandInfoToHelpString(prefix, selectedCommand);
                                            usedCommands.Add(selectedCommand);
                                        }
                                    }

                                    commands += "\n";
                                }

                                foreach (var category in totalCategories.Distinct())
                                {
                                    foreach (var selectedCommand in selectedCommands.Where(w => w.Attributes.OfType<CommandCategoriesAttribute>().Select(s => s.Categories).Any(a => a.Contains(category) && a.Length > 1)))
                                    {
                                        if (!usedCommands.Contains(selectedCommand))
                                        {
                                            commands += await CommandInfoToHelpString(prefix, selectedCommand);
                                            usedCommands.Add(selectedCommand);
                                        }
                                    }
                                }
                            }

                            if (selectedCategory == CommandCategory.General)
                            {
                                options.ForEach(x => x.IsDefault = false); // Reset to default
                                options.First(x => x.Option == selectedCategoryOrCommand).IsDefault = true;
                                await SetGeneralHelpEmbed(prefix);
                            }
                            else
                            {
                                this._embed.WithTitle(
                                    $"Overview of all {selectedCategory} commands");
                                this._embed.WithDescription(commands);
                                this._embed.Footer = null;
                                this._embed.Fields = new List<EmbedFieldBuilder>();
                            }
                        }
                    }
                    else
                    {
                        options.Where(w => w.Row == 2).ToList().ForEach(x => x.IsDefault = false); // Reset to default
                        options.First(x => x.Row == 2 && x.Option == selectedCategoryOrCommand).IsDefault = true;

                        var searchResult = this._service.Search(selectedCategoryOrCommand);
                        if (searchResult.IsSuccess && searchResult.Commands != null && searchResult.Commands.Any())
                        {
                            var userName = (this.Context.Message.Author as SocketGuildUser)?.Nickname ?? this.Context.User.Username;
                            this._embed.HelpResponse(searchResult.Commands[0].Command, prefix, userName);
                        }
                    }

                    var multiSelection = new MultiSelectionBuilder<string>()
                        .WithOptions(options)
                        .WithActionOnSuccess(ActionOnStop.None)
                        .WithActionOnTimeout(ActionOnStop.DisableInput)
                        .WithSelectionPage(PageBuilder.FromEmbed(this._embed.Build()))
                        .Build();

                    selectedResult =
                        await this.Interactivity.SendSelectionAsync(multiSelection, this.Context.Channel, message: message, timeout: TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds * 2));
                    message = selectedResult.Message;
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
            }
        }

        private async Task SetGeneralHelpEmbed(string prefix)
        {
            this._embedAuthor.WithIconUrl(this.Context.Client.CurrentUser?.GetAvatarUrl());
            this._embedAuthor.WithName(".fmbot help & command overview");
            this._embed.WithAuthor(this._embedAuthor);

            var description = new StringBuilder();
            var footer = new StringBuilder();

            description.AppendLine($"**Main command `{prefix}fm`**");
            description.AppendLine($"*Displays last scrobbles, and looks different depending on the mode you've set.*");

            description.AppendLine();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (contextUser == null)
            {
                description.AppendLine($"**Connecting your Last.fm account: `{prefix}login`**");
                description.AppendLine($"*Not receiving a DM from .fmbot when logging in? Please check if you have DMs enabled in this servers privacy settings.*");
            }
            else
            {
                description.AppendLine($"**Customizing your `{prefix}fm`**");
                description.AppendLine($"*For changing how your .fm command looks, use `{prefix}mode`.*");

                footer.AppendLine($"Logged in to .fmbot with the Last.fm account '{contextUser.UserNameLastFM}'");
            }

            description.AppendLine();

            description.AppendLine($"**Commands**");
            description.AppendLine($" - View all commands on [our website](https://fmbot.xyz/commands/)");
            description.AppendLine($" - Or use the dropdown below this message to pick a category");


            if (prefix != this._botSettings.Bot.Prefix)
            {
                description.AppendLine();
                description.AppendLine($"**Custom prefix:**");
                description.AppendLine($"*This server has the `{prefix}` prefix*");
                description.AppendLine($"Some examples of commands with this prefix are `{prefix}whoknows`, `{prefix}chart` and `{prefix}artisttracks`.");
            }

            description.AppendLine();
            description.AppendLine("**Links**");
            description.Append("[Help website](https://fmbot.xyz/) - ");

            var socketCommandContext = (SocketCommandContext)this.Context;
            var selfId = socketCommandContext.Client.CurrentUser.Id.ToString();
            description.Append("[Invite the bot](" +
                                   "https://discord.com/oauth2/authorize?" +
                                   $"client_id={selfId}" +
                                   "&scope=bot%20applications.commands" +
                                   $"&permissions={Constants.InviteLinkPermissions})");

            description.Append(" - [Get Supporter](https://opencollective.com/fmbot/contribute)");
            description.Append(" - [Support server](https://discord.gg/6y3jJjtDqK)");

            if (PublicProperties.IssuesAtLastFm)
            {
                this._embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues.\n" +
                                              ".fmbot is not affiliated with Last.fm.");
            }

            this._embed.WithDescription(description.ToString());
            this._embed.WithFooter(footer.ToString());
        }

        private static async Task<string> CommandInfoToHelpString(string prefix, CommandInfo commandInfo)
        {
            var firstAlias = commandInfo.Aliases.FirstOrDefault(f => f != commandInfo.Name && f.Length <= 4);
            if (firstAlias != null)
            {
                firstAlias = $" · `{firstAlias}`";
            }
            else
            {
                firstAlias = "";
            }

            if (commandInfo.Summary != null)
            {
                using var reader = new StringReader(commandInfo.Summary);
                var firstLine = await reader.ReadLineAsync();

                return $"**`{prefix}{commandInfo.Name}`{firstAlias}** | *{firstLine}*\n";
            }
            else
            {
                return $"**`{prefix}{commandInfo.Name}`{firstAlias}**\n";
            }
        }

        [Command("supporters", RunMode = RunMode.Async)]
        [Summary("Displays all .fmbot supporters.")]
        [Alias("donators", "donors", "backers")]
        [CommandCategories(CommandCategory.Other)]
        public async Task AllSupportersAsync()
        {
            var prefix = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            var supporters = await this._supporterService.GetAllVisibleSupporters();

            var supporterLists = supporters.ChunkBy(10);

            var description = new StringBuilder();
            description.AppendLine("Thank you to all our supporters that help keep .fmbot running. If you would like to be on this list too, please check out our [OpenCollective](https://opencollective.com/fmbot). \n" +
                                   $"For all information on donating to .fmbot you can check out `{prefix}donate`.");
            description.AppendLine();

            var pages = new List<PageBuilder>();
            foreach (var supporterList in supporterLists)
            {
                var supporterString = new StringBuilder();
                supporterString.Append(description.ToString());

                foreach (var supporter in supporterList)
                {
                    var type = supporter.SupporterType switch
                    {
                        SupporterType.Guild => " (server)",
                        SupporterType.User => "",
                        SupporterType.Company => " (business)",
                        _ => ""
                    };

                    supporterString.AppendLine($" - **{supporter.Name}** {type}");
                }

                pages.Add(new PageBuilder()
                    .WithDescription(supporterString.ToString())
                    .WithAuthor(this._embedAuthor)
                    .WithTitle(".fmbot supporters overview"));
            }

            this._embed.WithDescription(description.ToString());

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed();
        }

        [Command("countdown", RunMode = RunMode.Async)]
        [Summary("Counts down. Doesn't work that well above 3 seconds.")]
        [ExcludeFromHelp]
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
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(countdown + 20) >= DateTimeOffset.Now)
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
        [Summary("Displays all available commands.")]
        public async Task FullHelpAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            this._embed.WithDescription("**See a list of all available commands below.**\n" +
                                        $"Use `{prfx}serverhelp` to view all your configurable server settings.");

            foreach (var module in this._service.Modules
                .OrderByDescending(o => o.Commands.Count(w => !w.Attributes.OfType<ExcludeFromHelp>().Any() &&
                                                              !w.Attributes.OfType<ServerStaffOnly>().Any()))
                .Where(w =>
                !w.Attributes.OfType<ExcludeFromHelp>().Any() &&
                !w.Attributes.OfType<ServerStaffOnly>().Any()))
            {
                var moduleCommands = "";
                foreach (var cmd in module.Commands.Where(w =>
                    !w.Attributes.OfType<ExcludeFromHelp>().Any()))
                {
                    var result = await cmd.CheckPreconditionsAsync(this.Context);
                    if (result.IsSuccess)
                    {
                        if (!string.IsNullOrEmpty(moduleCommands))
                        {
                            moduleCommands += ", ";
                        }

                        var name = $"`{prfx}{cmd.Name}`";
                        name = name.Replace("fmfm", "fm");

                        moduleCommands += name;
                    }
                }

                var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $" - {module.Summary}";

                if (!string.IsNullOrEmpty(module.Name) && !string.IsNullOrEmpty(moduleCommands))
                {
                    this._embed.AddField(
                        module.Name + moduleSummary,
                        moduleCommands,
                        true);
                }

            }

            this._embed.WithFooter($"Add 'help' after a command to get more info. For example: '{prfx}chart help'");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }

        [Command("settinghelp", RunMode = RunMode.Async)]
        [Summary("Displays a list of all server settings.")]
        [Alias("serverhelp", "serversettings", "settings", "help server")]
        [CommandCategories(CommandCategory.Other)]
        public async Task ServerHelpAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            this._embed.WithDescription("**See all server settings below.**\n" +
                                        "These commands require either the `Admin` or the `Ban Members` permission.");

            foreach (var module in this._service.Modules
                .OrderByDescending(o => o.Commands.Count)
                .Where(w =>
                !w.Attributes.OfType<ExcludeFromHelp>().Any() &&
                w.Attributes.OfType<ServerStaffOnly>().Any()))
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

                        moduleCommands += $"`{prfx}{cmd.Name}`";
                    }
                }

                var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $" - {module.Summary}";

                if (!string.IsNullOrEmpty(module.Name) && !string.IsNullOrEmpty(moduleCommands))
                {
                    this._embed.AddField(
                        module.Name + moduleSummary,
                        moduleCommands,
                        true);
                }

            }

            this._embed.WithFooter($"Add 'help' after a command to get more info. For example: '{prfx}prefix help'");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }

        [Command("staffhelp", RunMode = RunMode.Async)]
        [Summary("Displays this list.")]
        [ExcludeFromHelp]
        public async Task StaffHelpAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            this._embed.WithDescription("**See all .fmbot staff commands below.**\n" +
                                        "These commands require .fmbot admin or owner.");

            foreach (var module in this._service.Modules
                .OrderByDescending(o => o.Commands.Count)
                .Where(w =>
                w.Attributes.OfType<ExcludeFromHelp>().Any()))
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

                        moduleCommands += $"`{prfx}{cmd.Name}`";
                    }
                }

                var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $" - {module.Summary}";

                if (!string.IsNullOrEmpty(module.Name) && !string.IsNullOrEmpty(moduleCommands))
                {
                    this._embed.AddField(
                        module.Name + moduleSummary,
                        moduleCommands,
                        true);
                }

            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }

        private static bool IsBotSelfHosted(ulong botId)
        {
            return !botId.Equals(Constants.BotProductionId) && !botId.Equals(Constants.BotDevelopId);
        }
    }
}
