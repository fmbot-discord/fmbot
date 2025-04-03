using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Serilog;
using Web.InternalApi;
using Enum = System.Enum;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.TextCommands;

[Name("Static commands")]
public class StaticCommands : BaseCommandModule
{
    private readonly CommandService _service;
    private readonly FriendsService _friendService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly MusicBotService _musicBotService;
    private readonly StaticBuilders _staticBuilders;
    private readonly StatusHandler.StatusHandlerClient _statusHandler;
    private readonly DiscordShardedClient _client;

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
        InteractiveService interactivity,
        MusicBotService musicBotService,
        StaticBuilders staticBuilders,
        StatusHandler.StatusHandlerClient statusHandler,
        DiscordShardedClient client) : base(botSettings)
    {
        this._friendService = friendsService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._service = service;
        this._supporterService = supporterService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._musicBotService = musicBotService;
        this._staticBuilders = staticBuilders;
        this._statusHandler = statusHandler;
        this._client = client;
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

        if (socketCommandContext.Client.CurrentUser.Id == Constants.BotBetaId)
        {
            embedDescription.AppendLine("The version of the bot you're currently using is the beta version, which is used to test new features and fixes.");
            embedDescription.AppendLine();

            embedDescription.AppendLine("Public invites for the beta version are currently closed. You can still add the normal main bot by **[clicking here](" +
                                        "https://discord.com/oauth2/authorize?" +
                                        $"client_id={Constants.BotProductionId}" +
                                        "&scope=bot%20applications.commands" +
                                        $"&permissions={Constants.InviteLinkPermissions}).**");
            embedDescription.AppendLine();
        }
        else
        {
            embedDescription.AppendLine("- You can invite .fmbot to your own server by **[clicking here](" +
                                        "https://discord.com/oauth2/authorize?" +
                                        $"client_id={selfId}" +
                                        "&scope=bot%20applications.commands" +
                                        $"&permissions={Constants.InviteLinkPermissions}).**");
            embedDescription.AppendLine("- Add .fmbot to your Discord account by **[clicking here](" +
                                        "https://discord.com/oauth2/authorize?" +
                                        $"client_id={selfId}" +
                                        "&scope=applications.commands" +
                                        "&integration_type=1).**");
        }

        embedDescription.AppendLine(
            "- Join the [.fmbot server](http://server.fmbot.xyz/) for support and updates.");

        embedDescription.AppendLine(
            $"- Help us cover hosting, development and other costs by getting [.fmbot supporter]({Constants.GetSupporterDiscordLink})");

        embedDescription.AppendLine(
            "- Check our [website](https://fmbot.xyz/) for more information.");

        this._embed.WithDescription(embedDescription.ToString());

        if (IsBotSelfHosted(socketCommandContext.Client.CurrentUser.Id))
        {
            this._embed.AddField("Note:",
                "This instance of .fmbot is self-hosted and could differ from the 'official' .fmbot. " +
                "The invite link linked here invites the self-hosted instance and not the official .fmbot.");
        }

        var components = new ComponentBuilder()
            .WithButton("Add to server", style: ButtonStyle.Link,
                url:
                $"https://discord.com/oauth2/authorize?client_id={selfId}&scope=bot%20applications.commands&permissions={Constants.InviteLinkPermissions}")
            .WithButton("Add to user", style: ButtonStyle.Link,
                url:
                $"https://discord.com/oauth2/authorize?client_id={selfId}&scope=applications.commands&integration_type=1");

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build(), components: components.Build());
        this.Context.LogCommandUsed();
    }

    [Command("source", RunMode = RunMode.Async)]
    [Summary("Shows links to the source code of .fmbot")]
    [Alias("github", "gitlab", "opensource", "sourcecode", "code")]
    [CommandCategories(CommandCategory.Other)]
    public async Task SourceAsync()
    {
        var embedDescription = new StringBuilder();
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        embedDescription.AppendLine(".fmbot is a source-available Discord bot.");
        embedDescription.AppendLine("The bot is written in C#, uses .NET 8 and Discord.Net.");

        this._embed.WithDescription(embedDescription.ToString());

        this._embed.AddField("Links",
            "[Main GitHub repository](https://github.com/fmbot-discord/fmbot/)\n" +
            "[Docs repository](https://github.com/fmbot-discord/docs)\n" +
            "[File an issue](https://github.com/fmbot-discord/fmbot/issues/new/choose)\n" +
            "[Development](https://fmbot.xyz/setup/)\n" +
            "[Supporter](https://fmbot.xyz/supporter)");

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("outofsync", RunMode = RunMode.Async)]
    [Summary("Info for what to do when now playing track is lagging behind")]
    [Alias("broken", "sync", "fix", "lagging", "stuck")]
    [CommandCategories(CommandCategory.Other)]
    public async Task OutOfSyncAsync([Remainder] string options = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("getsupporter", RunMode = RunMode.Async)]
    [Summary("Get the best .fmbot experience with Supporter")]
    [Alias("support", "patreon", "opencollective", "donations", "supporter", "donate")]
    [CommandCategories(CommandCategory.Other)]
    public async Task DonateAsync()
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = await this._staticBuilders.SupporterButtons(new ContextModel(this.Context, prfx, contextUser),
            true, false, true, "getsupporter");

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
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
        this._embedAuthor.WithName($"{selfUser.Username}");
        this._embedAuthor.WithUrl("https://fmbot.xyz/");

        this._embed.WithAuthor(this._embedAuthor);

        var currentProcess = Process.GetCurrentProcess();

        var startTime = DateTime.Now - currentProcess.StartTime;
        var currentMemoryUsage = currentProcess.WorkingSet64;
        var peakMemoryUsage = currentProcess.PeakWorkingSet64;

        var client = this.Context.Client as DiscordShardedClient;

        var ticks = Stopwatch.GetTimestamp();
        var upTime = (double)ticks / Stopwatch.Frequency;
        var upTimeInSeconds = TimeSpan.FromSeconds(upTime);

        var description = "";
        description += $"**Current Instance:** `{ConfigData.Data.Shards?.InstanceName}`\n";
        description += $"**Instance Uptime:** `{startTime.ToReadableString()}`\n";
        description += $"**Server Uptime:** `{upTimeInSeconds.ToReadableString()}`\n";
        description += $"**Usercount:** `{await this._userService.GetTotalUserCountAsync()}`  (Authorized: `{await this._userService.GetTotalAuthorizedUserCountAsync()}` | Discord: `{client.Guilds.Select(s => s.MemberCount).Sum()}`)\n";
        description += $"**Friendcount:** `{await this._friendService.GetTotalFriendCountAsync()}`\n";
        description += $"**Servercount:** `{client.Guilds.Count}`  (Shards: `{client.Shards.Count}` (`{client.GetShardIdFor(this.Context.Guild)}`))\n";
        description += $"**Memory usage:** `{currentMemoryUsage.ToFormattedByteString()}`  (Peak: `{peakMemoryUsage.ToFormattedByteString()}`)\n";

        var instanceOverviewDescription = new StringBuilder();
        try
        {
            var instanceOverview = await this._statusHandler.GetOverviewAsync(new Empty());

            foreach (var instance in instanceOverview.Instances.OrderBy(o => o.InstanceName.Length).ThenBy(o => o.InstanceName))
            {
                if (instance.LastHeartbeat.ToDateTime() >= DateTime.UtcNow.AddSeconds(-30))
                {
                    instanceOverviewDescription.Append(
                        $"✅ ");
                }
                else if (instance.LastHeartbeat.ToDateTime() >= DateTime.UtcNow.AddMinutes(-1))
                {
                    instanceOverviewDescription.Append(
                        $"{DiscordConstants.SamePosition}");
                }
                else
                {
                    instanceOverviewDescription.Append(
                        $"{DiscordConstants.OneToFiveDown}");
                }

                instanceOverviewDescription.Append(
                    $" `{instance.InstanceName}`");
                instanceOverviewDescription.Append(
                    $" - {instance.ConnectedShards}/{instance.TotalShards} shards");
                instanceOverviewDescription.Append(
                    $" - {instance.MemoryBytesUsed.ToFormattedByteString()}");
                instanceOverviewDescription.Append(
                    $" - <t:{instance.LastHeartbeat.ToDateTime().ToUnixEpochDate()}:R>");
                instanceOverviewDescription.AppendLine();
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in gRPC status fetch, {exceptionMessage}", e.Message, e);
            instanceOverviewDescription.AppendLine("Error");
        }

        this._embed.AddField("Instance heartbeat overview - connected/total", instanceOverviewDescription);

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

        var shardDescription = new StringBuilder();

        shardDescription.AppendLine(
            $"Total connected guilds: `{client.Guilds.Count()}`");
        shardDescription.AppendLine(
            $"Total shards: `{client.Shards.Count()}`");
        shardDescription.AppendLine(
            $"Connected shards: `{client.Shards.Count(c => c.ConnectionState == ConnectionState.Connected)}`");
        shardDescription.AppendLine(
            $"Disconnected shards: `{client.Shards.Count(c => c.ConnectionState == ConnectionState.Disconnected)}`");
        shardDescription.AppendLine(
            $"Connecting shards: `{client.Shards.Count(c => c.ConnectionState == ConnectionState.Connecting)}`");
        shardDescription.AppendLine(
            $"Disconnecting shards: `{client.Shards.Count(c => c.ConnectionState == ConnectionState.Disconnecting)}`");

        shardDescription.AppendLine();
        shardDescription.AppendLine(
            $"Min latency: `{client.Shards.Select(s => s.Latency).Min() + "ms`"}");
        shardDescription.AppendLine(
            $"Average latency: `{Math.Round(client.Shards.Select(s => s.Latency).Average(), 2) + "ms`"}");
        shardDescription.AppendLine(
            $"Max latency: `{client.Shards.Select(s => s.Latency).Max() + "ms`"}");

        try
        {
            if (client.Shards.Count(c => c.ConnectionState == ConnectionState.Disconnecting) > 0)
            {
                var disconnecting = new StringBuilder();
                foreach (var shard in client.Shards.Where(w => w.ConnectionState == ConnectionState.Disconnecting).Take(8))
                {
                    disconnecting.Append($"`{shard.ShardId}` - ");
                }
                this._embed.AddField("Disconnecting shards", disconnecting.ToString());
            }

            if (client.Shards.Count(c => c.ConnectionState == ConnectionState.Disconnected) > 0)
            {
                var disconnected = new StringBuilder();
                foreach (var shard in client.Shards.Where(w => w.ConnectionState == ConnectionState.Disconnecting).Take(8))
                {
                    disconnected.Append($"`{shard.ShardId}` - ");
                }
                this._embed.AddField("Disconnected shards", disconnected.ToString());
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in shards command", e);
        }

        if (ConfigData.Data.Shards?.TotalShards != null)
        {
            var shardConfig = new StringBuilder();
            shardConfig.AppendLine($"Total shards: `{ConfigData.Data.Shards?.TotalShards}`");
            shardConfig.AppendLine($"First shard: `{ConfigData.Data.Shards?.StartShard}`");
            shardConfig.AppendLine($"Last shard: `{ConfigData.Data.Shards?.EndShard}`");
            shardConfig.AppendLine($"Instance: `{ConfigData.Data.Shards?.InstanceName}`");
            shardConfig.AppendLine($"Main instance: `{ConfigData.Data.Shards?.MainInstance}`");

            this._embed.AddField("Instance config", shardConfig.ToString());
        }

        this._embed.WithDescription(shardDescription.ToString());
        if (this.Context.Guild != null)
        {
            this._embed.WithFooter(
                $"Guild {this.Context.Guild.Name} | {this.Context.Guild.Id} is on shard {client.GetShardIdFor(this.Context.Guild)}");
        }

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("debugbotscrobbling", RunMode = RunMode.Async)]
    [Alias("debugbotscrobble", "debugbotscrobbles", "botscrobbledebug", "botscrobblingdebug")]
    [Summary("Debugging for bot scrobbling")]
    [ExcludeFromHelp]
    public async Task DebugBotScrobbles()
    {
        var logs = this._musicBotService.BotScrobblingLogs.Where(w => w.GuildId == this.Context.Guild.Id);

        var logPages = logs.OrderByDescending(o => o.DateTime).Chunk(25).ToList();
        var pageCounter = 1;

        var pages = new List<PageBuilder>();
        foreach (var logPage in logPages)
        {
            var description = new StringBuilder();

            foreach (var log in logPage)
            {
                description.AppendLine($"<t:{log.DateTime.ToUnixEpochDate()}:R> | {log.Log}");
            }

            pages.Add(new PageBuilder()
                .WithDescription(description.ToString())
                .WithFooter($"Page {pageCounter}/{logPages.Count()}")
                .WithTitle($"Bot scrobbling debug log for {this.Context.Guild.Name} | {this.Context.Guild.Id}"));

            pageCounter++;
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No bot scrobbling logs yet, make sure fmbot can see the 'Now playing' message")
                .WithFooter($"Page {pageCounter}/{logPages.Count()}")
                .WithTitle($"Bot scrobbling debug log for {this.Context.Guild.Name} | {this.Context.Guild.Id}"));
        }

        var paginator = StringService.BuildStaticPaginator(pages);

        _ = this.Interactivity.SendPaginatorAsync(
            paginator.Build(),
            this.Context.Channel,
            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

        this.Context.LogCommandUsed();
    }

    [Command("shard", RunMode = RunMode.Async)]
    [Summary("Displays shard info for a specific guild")]
    [GuildOnly]
    [ExcludeFromHelp]
    [Alias("shardinfo")]
    [Examples("shard 0", "shard 821660544581763093")]
    public async Task ShardInfoAsync(ulong? guildId = null)
    {
        if (!guildId.HasValue)
        {
            await this.Context.Channel.SendMessageAsync($"Enter a server id please (this server is `{this.Context.Guild.Id}`)");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var client = this.Context.Client as DiscordShardedClient;

        DiscordSocketClient shard;

        if (guildId is < 1000 and >= 0)
        {
            shard = client.GetShard(int.Parse(guildId.Value.ToString()));
        }
        else
        {
            var guild = client.GetGuild(guildId.Value);
            shard = client.GetShardFor(guild);
            this._embed.WithFooter($"{guild.Name} - {guild.MemberCount} members");
        }

        if (shard != null)
        {
            this._embed.WithDescription($"Guild/shard `{guildId}` info:\n\n" +
                                        $"Shard id: `{shard.ShardId}`\n" +
                                        $"Latency: `{shard.Latency}ms`\n" +
                                        $"Guilds: `{shard.Guilds.Count}`\n" +
                                        $"Connection state: `{shard.ConnectionState}`");

        }
        else
        {
            await this.Context.Channel.SendMessageAsync("Server or shard could not be found. \n" +
                                                        "This either means the bot is not connected to that server or that the bot is not in this server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("help", RunMode = RunMode.Async)]
    [Summary("Quick help summary to get started.")]
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
                var userName = (this.Context.Message.Author as SocketGuildUser)?.DisplayName ?? this.Context.User.GlobalName ?? this.Context.User.Username;
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
                            this._embed.Fields = new List<EmbedFieldBuilder>();
                            await SetGeneralHelpEmbed(prefix);
                        }
                        else
                        {
                            this._embed.WithTitle(
                                $"Overview of all {selectedCategory} commands");
                            this._embed.WithDescription(commands);
                            this._embed.Footer = null;
                            this._embed.Fields = new List<EmbedFieldBuilder>();
                            if (selectedCategory == CommandCategory.Importing)
                            {
                                this._embed.AddField("Slash commands:",
                                    "**`/import spotify`** | *Starts your Spotify import*\n" +
                                    "**`/import applemusic`** | *Starts your Apple Music import*\n" +
                                    "**`/import manage`** | *Manage and configure your existing imports*");
                            }
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
                        var userName = (this.Context.Message.Author as SocketGuildUser)?.DisplayName ?? this.Context.User.GlobalName ?? this.Context.User.Username;
                        this._embed.Fields = new List<EmbedFieldBuilder>();
                        this._embed.HelpResponse(searchResult.Commands[0].Command, prefix, userName);
                    }
                }

                var multiSelection = new MultiSelectionBuilder<string>()
                    .WithOptions(options)
                    .WithActionOnSuccess(ActionOnStop.None)
                    .WithActionOnTimeout(ActionOnStop.DisableInput)
                    .WithSelectionPage(PageBuilder.FromEmbed(this._embed.Build()))
                    .Build();

                selectedResult = message is null
                    ? await this.Interactivity.SendSelectionAsync(multiSelection, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds * 2))
                    : await this.Interactivity.SendSelectionAsync(multiSelection, message, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds * 2));

                message = selectedResult.Message;
            }

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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
            description.AppendLine($"**Connecting a Last.fm account**");
            description.AppendLine($"To use .fmbot, you have to connect a Last.fm account. Last.fm is a website that tracks what music you listen to. Get started with `{prefix}login`.");
        }
        else
        {
            description.AppendLine($"**Customizing .fmbot**");
            description.AppendLine($"- User settings: `{prefix}settings`");
            description.AppendLine($"- Server config: `{prefix}configuration`");

            footer.AppendLine($"Logged in to .fmbot with the Last.fm account '{contextUser.UserNameLastFM}'");
        }

        description.AppendLine();

        description.AppendLine($"**Commands**");
        description.AppendLine($"- View all commands on [our website](https://fmbot.xyz/commands/)");
        description.AppendLine($"- Or use the dropdown below this message to pick a category");

        if (prefix != this._botSettings.Bot.Prefix)
        {
            description.AppendLine();
            description.AppendLine($"**Custom prefix:**");
            description.AppendLine($"*This server has the `{prefix}` prefix*");
            description.AppendLine($"Some examples of commands with this prefix are `{prefix}whoknows`, `{prefix}chart` and `{prefix}artisttracks`.");
        }

        description.AppendLine();
        description.AppendLine("**Links**");
        description.Append("[Website](https://fmbot.xyz/) - ");

        var socketCommandContext = (SocketCommandContext)this.Context;
        var selfId = socketCommandContext.Client.CurrentUser.Id.ToString();
        description.Append("[Add to server](" +
                           "https://discord.com/oauth2/authorize?" +
                           $"client_id={selfId}" +
                           "&scope=bot%20applications.commands" +
                           $"&permissions={Constants.InviteLinkPermissions})");
        description.Append(" - [Add to user](" +
                           "https://discord.com/oauth2/authorize?" +
                           $"client_id={selfId}" +
                           "&scope=applications.commands" +
                           "&integration_type=1)");

        description.Append($" - [Get Supporter]({Constants.GetSupporterDiscordLink})");
        description.Append(" - [Support server](https://discord.gg/6y3jJjtDqK)");

        if (PublicProperties.IssuesAtLastFm)
        {
            var issues = "";
            if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
            {
                issues = "\n\n" +
                         "Note:\n" +
                         $"*\"{PublicProperties.IssuesReason}\"*";
            }

            this._embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues.\n" +
                                          $".fmbot is not affiliated with Last.fm.{issues}");
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
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = await this._staticBuilders.SupportersAsync(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
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

    [Command("givemefish", RunMode = RunMode.Async)]
    [Summary("Fish fish. Blub blub.")]
    [ExcludeFromHelp]
    public async Task FishAsync([Remainder] string extraValues = null)
    {
        var reply = new StringBuilder();

        var random1 = RandomNumberGenerator.GetInt32(1, 10);
        switch (random1)
        {
            case 1:
                reply.AppendLine("🦈 Looks like a shark!");
                break;
            case 2:
                reply.AppendLine("🐟 blub blub. It's a fish");
                break;
            case 3:
                reply.AppendLine("🐠 Wow, a tropical fish!");
                break;
            case 4:
                reply.AppendLine("🦈 omg watch out a shark!");
                break;
            case 5:
                reply.AppendLine("<:blahaj_shark:969501603142983710> it's a real blahaj!");
                break;
            case 6:
                reply.AppendLine("<:lobster:1161015424322908360> A lobster? Does that even count as fish?");
                break;
            case 7:
                reply.AppendLine("🐡 A blowfish. Amazing.");
                break;
            case 8:
                reply.AppendLine("🦐 It's very shrimple. You got a shrimp.");
                break;
            case 9:
                reply.AppendLine("🐳 A whale! It looks happy.");
                break;

        }

        reply.AppendLine();

        var random2 = RandomNumberGenerator.GetInt32(1, 9);
        switch (random2)
        {
            case 1:
                reply.AppendLine("*You got scared and threw it back into the water.*");
                break;
            case 2:
                reply.AppendLine("*It looks sad, so you let the fish go so it can mind it's own business.*");
                break;
            case 3:
                reply.AppendLine("*It whispered, \"I have important fishy business to attend to.\", so you throw it back.*");
                break;
            case 4:
                reply.AppendLine("*You felt a strong connection with the fish and decided it was your fishy soulmate, so you let it swim freely.*");
                break;
            case 5:
                reply.AppendLine("*You noticed that it was sleep scrobbling, which is not really your cup of tea. Let's try that again.*");
                break;
            case 6:
                reply.AppendLine("*Seems like someone else caught the fish before you did. Looks like a certain member of LOONA...*");
                break;
            case 7:
                reply.AppendLine("*Wow, it's super heavy! Better to let it swim freely.*");
                break;
            case 8:
                reply.AppendLine("*It's scrobbling 'Rolling in the Deep' from Adele. Sounds like it belongs in the water.*");
                break;
        }

        var random3 = RandomNumberGenerator.GetInt32(1, 6);
        switch (random3)
        {
            case 1:
                this._embed.WithColor(new Color(6, 66, 115));
                break;
            case 2:
                this._embed.WithColor(new Color(118, 182, 196));
                break;
            case 3:
                this._embed.WithColor(new Color(127, 205, 255));
                break;
            case 4:
                this._embed.WithColor(new Color(29, 162, 216));
                break;
            case 5:
                this._embed.WithColor(new Color(222, 243, 246));
                break;
        }

        this._embed.WithTitle("You caught a fish!");
        this._embed.WithDescription(reply.ToString());

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
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

            var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $"- {module.Summary}";

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

            var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $"- {module.Summary}";

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

            var moduleSummary = string.IsNullOrEmpty(module.Summary) ? "" : $"- {module.Summary}";

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
        return !botId.Equals(Constants.BotProductionId) && !botId.Equals(Constants.BotBetaId);
    }
}
