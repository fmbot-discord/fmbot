using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.Commands;
using Serilog;
using Web.InternalApi;

namespace FMBot.Bot.TextCommands;

[ModuleName("Static commands")]
public class StaticCommands(
    CommandService<CommandContext> service,
    FriendsService friendsService,
    GuildService guildService,
    IPrefixService prefixService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity,
    MusicBotService musicBotService,
    StaticBuilders staticBuilders,
    StatusHandler.StatusHandlerClient statusHandler,
    ShardedGatewayClient client)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    private static readonly List<DateTimeOffset> StackCooldownTimer = [];
    private static readonly List<User> StackCooldownTarget = [];

    [Command("invite", "server", "info")]
    [Summary("Info for inviting the bot to a server")]
    [CommandCategories(CommandCategory.Other)]
    public async Task InviteAsync()
    {
        var selfId = client.Id.ToString();
        var embedDescription = new StringBuilder();
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        if (client.Id == Constants.BotBetaId)
        {
            embedDescription.AppendLine(
                "The version of the bot you're currently using is the beta version, which is used to test new features and fixes.");
            embedDescription.AppendLine();

            embedDescription.AppendLine(
                "Public invites for the beta version are currently closed. You can still add the normal main bot by **[clicking here](" +
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
            "- Join the [.fmbot server](https://discord.gg/fmbot) for support and updates.");

        embedDescription.AppendLine(
            $"- Help us cover hosting, development and other costs by getting [.fmbot supporter]({Constants.GetSupporterDiscordLink})");

        embedDescription.AppendLine(
            "- Check our [website](https://fm.bot/) for more information.");

        this._embed.WithDescription(embedDescription.ToString());

        var components = new ActionRowProperties()
            .WithButton(
                $"https://discord.com/oauth2/authorize?client_id={selfId}&scope=bot%20applications.commands&permissions={Constants.InviteLinkPermissions}",
                "Add to server")
            .WithButton($"https://discord.com/oauth2/authorize?client_id={selfId}&scope=applications.commands&integration_type=1",
                "Add to user");

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
        {
            Embeds = [this._embed],
            Components = [components]
        });
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("source", "github", "gitlab", "opensource", "sourcecode", "code")]
    [Summary("Shows links to the source code of .fmbot")]
    [CommandCategories(CommandCategory.Other)]
    public async Task SourceAsync()
    {
        var embedDescription = new StringBuilder();
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        embedDescription.AppendLine(".fmbot is a source-available Discord bot.");
        embedDescription.AppendLine("The bot is written in C#, uses .NET 9 and Discord.Net.");

        this._embed.WithDescription(embedDescription.ToString());

        this._embed.AddField("Links",
            "[Main GitHub repository](https://github.com/fmbot-discord/fmbot/)\n" +
            "[Docs repository](https://github.com/fmbot-discord/docs)\n" +
            "[File an issue](https://github.com/fmbot-discord/fmbot/issues/new/choose)\n" +
            "[Development](https://fm.bot/setup/)\n" +
            "[Supporter](https://fm.bot/supporter)");

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("outofsync", "broken", "sync", "fix", "lagging", "stuck")]
    [Summary("Info for what to do when now playing track is lagging behind")]
    [CommandCategories(CommandCategory.Other)]
    public async Task OutOfSyncAsync([CommandParameter(Remainder = true)] string options = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);

        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("getsupporter", "support", "patreon", "opencollective", "donations", "supporter")]
    [Summary("Get the best .fmbot experience with Supporter")]
    [CommandCategories(CommandCategory.Other)]
    public async Task GetSupporter()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var response = await staticBuilders.SupporterButtons(new ContextModel(this.Context, prfx, contextUser),
            true, false, true, "getsupporter");

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("status")]
    [Summary("Displays bot stats.")]
    [CommandCategories(CommandCategory.Other)]
    public async Task StatusAsync()
    {
        var selfUser = client.GetCurrentUser();
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        this._embedAuthor.WithIconUrl(selfUser?.GetAvatarUrl()?.ToString());
        this._embedAuthor.WithName($"{selfUser?.Username}");
        this._embedAuthor.WithUrl("https://fm.bot/");

        this._embed.WithAuthor(this._embedAuthor);

        var currentProcess = Process.GetCurrentProcess();

        var startTime = DateTime.Now - currentProcess.StartTime;
        var currentMemoryUsage = currentProcess.WorkingSet64;
        var peakMemoryUsage = currentProcess.PeakWorkingSet64;

        var ticks = Stopwatch.GetTimestamp();
        var upTime = (double)ticks / Stopwatch.Frequency;
        var upTimeInSeconds = TimeSpan.FromSeconds(upTime);

        var totalGuilds = client.Sum(shard => shard.Cache.Guilds.Count);
        var totalMembers = client.SelectMany(shard => shard.Cache.Guilds.Values).Sum(g => g.ApproximateUserCount ?? 0);
        var shardCount = client.Count();
        var currentShardId = this.Context.Guild != null ? GetShardIdForGuild(this.Context.Guild.Id, shardCount) : 0;

        var description = "";
        description += $"**Current Instance:** `{ConfigData.Data.Shards?.InstanceName}`\n";
        description += $"**Instance Uptime:** `{startTime.ToReadableString()}`\n";
        description += $"**Server Uptime:** `{upTimeInSeconds.ToReadableString()}`\n";
        description +=
            $"**Usercount:** `{await userService.GetTotalUserCountAsync()}`  (Authorized: `{await userService.GetTotalAuthorizedUserCountAsync()}` | Discord: `{totalMembers}`)\n";
        description += $"**Friendcount:** `{await friendsService.GetTotalFriendCountAsync()}`\n";
        description +=
            $"**Servercount:** `{totalGuilds}`  (Shards: `{shardCount}` (`{currentShardId}`))\n";
        description +=
            $"**Memory usage:** `{currentMemoryUsage.ToFormattedByteString()}`  (Peak: `{peakMemoryUsage.ToFormattedByteString()}`)\n";

        var instanceOverviewDescription = new StringBuilder();
        try
        {
            var instanceOverview = await statusHandler.GetOverviewAsync(new Empty());

            foreach (var instance in instanceOverview.Instances.OrderBy(o => o.InstanceName.Length)
                         .ThenBy(o => o.InstanceName))
            {
                if (instance.LastHeartbeat.ToDateTime() >= DateTime.UtcNow.AddSeconds(-30))
                {
                    instanceOverviewDescription.Append(
                        $"✅ ");
                }
                else if (instance.LastHeartbeat.ToDateTime() >= DateTime.UtcNow.AddMinutes(-1))
                {
                    instanceOverviewDescription.Append(
                        $"{EmojiProperties.Custom(DiscordConstants.SamePosition).ToDiscordString("same_position")}");
                }
                else
                {
                    instanceOverviewDescription.Append(
                        $"{EmojiProperties.Custom(DiscordConstants.OneToFiveDown).ToDiscordString("one_to_five_down")}");
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
            Log.Error(e, "Error in gRPC status fetch, {exceptionMessage}", e.Message);
            instanceOverviewDescription.AppendLine("Error");
        }

        this._embed.AddField("Instance heartbeat overview - connected/total", instanceOverviewDescription.ToString());

        this._embed.WithDescription(description);

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("shards")]
    [Summary("Displays bot sharding info.")]
    [ExcludeFromHelp]
    public async Task ShardsAsync()
    {
        this._embed.WithTitle("Bot instance shards");

        var shards = client.ToList();
        var totalGuilds = shards.Sum(s => s.Cache.Guilds.Count);
        var shardCount = shards.Count;

        var shardDescription = new StringBuilder();

        shardDescription.AppendLine(
            $"Total connected guilds: `{totalGuilds}`");
        shardDescription.AppendLine(
            $"Total shards: `{shardCount}`");

        shardDescription.AppendLine();
        if (shards.Any())
        {
            var latencies = shards.Select(s => s.Latency.TotalMilliseconds).ToList();
            shardDescription.AppendLine(
                $"Min latency: `{latencies.Min():F0}ms`");
            shardDescription.AppendLine(
                $"Average latency: `{Math.Round(latencies.Average(), 2)}ms`");
            shardDescription.AppendLine(
                $"Max latency: `{latencies.Max():F0}ms`");
        }

        try
        {
            // Note: NetCord doesn't expose connection state directly on shards
            // All shards in the collection are connected
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in shards command");
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
            var currentShardId = GetShardIdForGuild(this.Context.Guild.Id, shardCount);
            this._embed.WithFooter(
                $"Guild {this.Context.Guild.Name} | {this.Context.Guild.Id} is on shard {currentShardId}");
        }

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("debugbotscrobbling", "debugbotscrobble", "debugbotscrobbles", "botscrobbledebug", "botscrobblingdebug")]
    [Summary("Debugging for bot scrobbling")]
    [ExcludeFromHelp]
    [GuildOnly]
    public async Task DebugBotScrobbles()
    {
        var logs = musicBotService.BotScrobblingLogs.Where(w => w.GuildId == this.Context.Guild.Id);

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

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("shard", "shardinfo")]
    [Summary("Displays shard info for a specific guild")]
    [GuildOnly]
    [ExcludeFromHelp]
    [Examples("shard 0", "shard 821660544581763093")]
    public async Task ShardInfoAsync(ulong? guildId = null)
    {
        if (!guildId.HasValue)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId,
                $"Enter a server id please (this server is `{this.Context.Guild.Id}`)");
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var shards = client.ToList();
        var shardCount = shards.Count;
        GatewayClient shard = null;

        if (guildId is < 1000 and >= 0)
        {
            var shardIndex = (int)guildId.Value;
            if (shardIndex < shards.Count)
            {
                shard = shards[shardIndex];
            }
        }
        else
        {
            // Find the shard that has this guild cached
            shard = shards.FirstOrDefault(s => s.Cache.Guilds.ContainsKey(guildId.Value));
            if (shard != null)
            {
                var guild = shard.Cache.Guilds[guildId.Value];
                this._embed.WithFooter($"{guild.Name} - {guild.ApproximateUserCount ?? 0} members");
            }
        }

        if (shard != null)
        {
            var shardGuildCount = shard.Cache.Guilds.Count;
            this._embed.WithDescription($"Guild/shard `{guildId}` info:\n\n" +
                                        $"Shard id: `{shard.Id}`\n" +
                                        $"Latency: `{shard.Latency.TotalMilliseconds:F0}ms`\n" +
                                        $"Guilds: `{shardGuildCount}`\n" +
                                        $"Connection state: `Connected`");
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Content = "Server or shard could not be found. \n" +
                          "This either means the bot is not connected to that server or that the bot is not in this server."
            });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("help")]
    [Summary("Quick help summary to get started.")]
    [CommandCategories(CommandCategory.Other)]
    public async Task HelpAsync([CommandParameter(Remainder = true)] string extraValues = null)
    {
        var prefix = prefixService.GetPrefix(this.Context.Guild?.Id);
        var allCommands = service.GetCommands().SelectMany(kvp => kvp.Value).ToList();
        var userName = GetUserDisplayName(this.Context.Message.Author as GuildUser, this.Context.User);

        try
        {
            string selectedCommand = null;
            if (!string.IsNullOrWhiteSpace(extraValues))
            {
                if (extraValues.Length > prefix.Length && extraValues.Contains(prefix))
                {
                    extraValues = extraValues.Replace(prefix, "");
                }
                selectedCommand = extraValues.Trim();
            }

            var response = await staticBuilders.BuildHelpResponse(
                allCommands,
                prefix,
                null,
                selectedCommand,
                userName,
                this.Context.User.Id);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Embeds = [response.Embed],
                Components = response.GetMessageComponents()
            });

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("supporters", "donators", "donors", "backers")]
    [Summary("Displays all .fmbot supporters.")]
    [CommandCategories(CommandCategory.Other)]
    public async Task AllSupportersAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);

        var response = await staticBuilders.SupportersAsync(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("countdown")]
    [Summary("Counts down. Doesn't work that well above 3 seconds.")]
    [ExcludeFromHelp]
    public async Task CountdownAsync(int countdown = 3)
    {
        if (guildService.CheckIfDM(this.Context))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Command is not supported in DMs." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotSupportedInDm }, userService);
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

        var msg = this.Context.Message;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(countdown + 20) >=
                DateTimeOffset.Now)
            {
                var authorIndex = StackCooldownTarget.FindIndex(u => u.Id == this.Context.Message.Author.Id);
                var secondsLeft = (int)(StackCooldownTimer[authorIndex]
                    .AddSeconds(countdown + 30) - DateTimeOffset.Now).TotalSeconds;
                if (secondsLeft <= 20)
                {
                    var secondString = secondsLeft == 1 ? "second" : "seconds";
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Please wait {secondsLeft} {secondString} before starting another countdown." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
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

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Countdown for `{countdown}` seconds starting!" });
        await Task.Delay(4000);

        for (var i = countdown; i > 0; i--)
        {
            _ = this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = i.ToString() });
            await Task.Delay(1000);
        }

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Go!" });
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("givemefish")]
    [Summary("Fish fish. Blub blub.")]
    [ExcludeFromHelp]
    public async Task FishAsync([CommandParameter(Remainder = true)] string extraValues = null)
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
                reply.AppendLine(
                    "*It whispered, \"I have important fishy business to attend to.\", so you throw it back.*");
                break;
            case 4:
                reply.AppendLine(
                    "*You felt a strong connection with the fish and decided it was your fishy soulmate, so you let it swim freely.*");
                break;
            case 5:
                reply.AppendLine(
                    "*You noticed that it was sleep scrobbling, which is not really your cup of tea. Let's try that again.*");
                break;
            case 6:
                reply.AppendLine(
                    "*Seems like someone else caught the fish before you did. Looks like a certain member of LOONA...*");
                break;
            case 7:
                reply.AppendLine("*Wow, it's super heavy! Better to let it swim freely.*");
                break;
            case 8:
                reply.AppendLine(
                    "*It's scrobbling 'Rolling in the Deep' from Adele. Sounds like it belongs in the water.*");
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

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("fullhelp")]
    [Summary("Displays all available commands.")]
    public async Task FullHelpAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        this._embed.WithDescription("**See a list of all available commands below.**\n" +
                                    $"Use `{prfx}serverhelp` to view all your configurable server settings.");

        var allCommands = service.GetCommands().SelectMany(kvp => kvp.Value)
            .Where(w => !GetAllAttributes(w).OfType<ExcludeFromHelp>().Any() &&
                        !GetAllAttributes(w).OfType<ServerStaffOnly>().Any())
            .ToList();

        // Group commands by their module name attribute
        var commandsByModule = allCommands
            .GroupBy(c => GetAllAttributes(c).OfType<ModuleNameAttribute>().FirstOrDefault()?.ModuleName ?? "Other")
            .OrderByDescending(g => g.Count());

        foreach (var moduleGroup in commandsByModule)
        {
            var moduleCommands = "";
            foreach (var cmd in moduleGroup)
            {
                if (!string.IsNullOrEmpty(moduleCommands))
                {
                    moduleCommands += ", ";
                }

                var cmdName = GetCommandName(cmd);
                var name = $"`{prfx}{cmdName}`";
                name = name.Replace("fmfm", "fm");

                moduleCommands += name;
            }

            if (!string.IsNullOrEmpty(moduleGroup.Key) && !string.IsNullOrEmpty(moduleCommands))
            {
                this._embed.AddField(moduleGroup.Key, moduleCommands, true);
            }
        }

        this._embed.WithFooter($"Add 'help' after a command to get more info. For example: '{prfx}chart help'");
        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("settinghelp", "serverhelp", "serversettings", "settings")]
    [Summary("Displays a list of all server settings.")]
    [CommandCategories(CommandCategory.Other)]
    public async Task ServerHelpAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        this._embed.WithDescription("**See all server settings below.**\n" +
                                    "These commands require either the `Admin` or the `Ban Members` permission.");

        var allCommands = service.GetCommands().SelectMany(kvp => kvp.Value)
            .Where(w => !GetAllAttributes(w).OfType<ExcludeFromHelp>().Any() &&
                        GetAllAttributes(w).OfType<ServerStaffOnly>().Any())
            .ToList();

        // Group commands by their module name attribute
        var commandsByModule = allCommands
            .GroupBy(c => GetAllAttributes(c).OfType<ModuleNameAttribute>().FirstOrDefault()?.ModuleName ?? "Server Settings")
            .OrderByDescending(g => g.Count());

        foreach (var moduleGroup in commandsByModule)
        {
            var moduleCommands = "";
            foreach (var cmd in moduleGroup)
            {
                if (!string.IsNullOrEmpty(moduleCommands))
                {
                    moduleCommands += ", ";
                }

                moduleCommands += $"`{prfx}{GetCommandName(cmd)}`";
            }

            if (!string.IsNullOrEmpty(moduleGroup.Key) && !string.IsNullOrEmpty(moduleCommands))
            {
                this._embed.AddField(moduleGroup.Key, moduleCommands, true);
            }
        }

        this._embed.WithFooter($"Add 'help' after a command to get more info. For example: '{prfx}prefix help'");
        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("staffhelp")]
    [Summary("Displays this list.")]
    [ExcludeFromHelp]
    public async Task StaffHelpAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        this._embed.WithDescription("**See all .fmbot staff commands below.**\n" +
                                    "These commands require .fmbot admin or owner.");

        var allCommands = service.GetCommands().SelectMany(kvp => kvp.Value)
            .Where(w => GetAllAttributes(w).OfType<ExcludeFromHelp>().Any())
            .ToList();

        // Group commands by their module name attribute
        var commandsByModule = allCommands
            .GroupBy(c => GetAllAttributes(c).OfType<ModuleNameAttribute>().FirstOrDefault()?.ModuleName ?? "Staff")
            .OrderByDescending(g => g.Count());

        foreach (var moduleGroup in commandsByModule)
        {
            var moduleCommands = "";
            foreach (var cmd in moduleGroup)
            {
                if (!string.IsNullOrEmpty(moduleCommands))
                {
                    moduleCommands += ", ";
                }

                moduleCommands += $"`{prfx}{GetCommandName(cmd)}`";
            }

            if (!string.IsNullOrEmpty(moduleGroup.Key) && !string.IsNullOrEmpty(moduleCommands))
            {
                this._embed.AddField(moduleGroup.Key, moduleCommands, true);
            }
        }

        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    private static bool IsBotSelfHosted(ulong botId)
    {
        return !botId.Equals(Constants.BotProductionId) && !botId.Equals(Constants.BotBetaId);
    }

    // Helper methods for NetCord CommandInfo access
    private static int GetShardIdForGuild(ulong guildId, int totalShards)
    {
        return (int)((guildId >> 22) % (ulong)totalShards);
    }

    private static string GetUserDisplayName(GuildUser guildUser, User user)
    {
        return guildUser?.GetDisplayName() ?? user?.GetDisplayName() ?? "Unknown";
    }

    private static ICommandInfo<CommandContext> FindCommand(IEnumerable<ICommandInfo<CommandContext>> commands, string name)
    {
        return commands.FirstOrDefault(c =>
            GetCommandName(c).Equals(name, StringComparison.OrdinalIgnoreCase) ||
            GetCommandAliases(c).Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<Attribute> GetAllAttributes(ICommandInfo<CommandContext> commandInfo)
    {
        return commandInfo.Attributes.Values.SelectMany(x => x);
    }

    private static string GetCommandName(ICommandInfo<CommandContext> commandInfo)
    {
        // NetCord stores name differently - check for Name property via reflection or use aliases
        var nameAttr = GetAllAttributes(commandInfo).OfType<CommandAttribute>().FirstOrDefault();
        if (nameAttr != null && nameAttr.Aliases.Length > 0)
        {
            return nameAttr.Aliases[0];
        }
        return commandInfo.ToString() ?? "unknown";
    }

    private static IEnumerable<string> GetCommandAliases(ICommandInfo<CommandContext> commandInfo)
    {
        var nameAttr = GetAllAttributes(commandInfo).OfType<CommandAttribute>().FirstOrDefault();
        return nameAttr?.Aliases ?? Array.Empty<string>();
    }

    private static string GetCommandSummary(ICommandInfo<CommandContext> commandInfo)
    {
        return GetAllAttributes(commandInfo).OfType<SummaryAttribute>().FirstOrDefault()?.Summary;
    }
}
