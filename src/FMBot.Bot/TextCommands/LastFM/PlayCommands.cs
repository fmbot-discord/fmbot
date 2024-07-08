using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Plays")]
public class PlayCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildBuilders _guildBuilders;
    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();


    public PlayCommands(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        SettingService settingService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        PlayBuilder playBuilder,
        GuildBuilders guildBuilders) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._dataSourceFactory = dataSourceFactory;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._playBuilder = playBuilder;
        this._guildBuilders = guildBuilders;
    }

    [Command("fm", RunMode = RunMode.Async)]
    [Summary("Shows you or someone else their current track")]
    [Alias("np", "qm", "wm", "em", "rm", "tm", "ym", "om", "pm", "gm", "sm", "hm", "jm", "km",
        "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "nowplaying")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task NowPlayingAsync([Remainder] string options = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser?.UserNameLastFM == null)
        {
            var userNickname = (this.Context.User as SocketGuildUser)?.DisplayName;
            this._embed.UsernameNotSetErrorResponse(prfx, userNickname ?? this.Context.User.GlobalName ?? this.Context.User.Username);

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
            return;
        }
        if (options == "help")
        {
            var fmString = "fm";
            if (prfx == ".fm")
            {
                fmString = "";
            }

            var commandName = prfx + fmString;

            this._embed.WithTitle($"Information about the '{commandName}' command");
            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            this._embed.WithDescription("Displays what you or someone else is listening to.");

            this._embed.AddField("Examples",
                $"`{prfx}fm`\n" +
                $"`{prfx}nowplaying`\n" +
                $"`{prfx}fm lfm:fm-bot`\n" +
                $"`{prfx}fm @user`");

            this._embed.AddField("Options", $"To customize how this command looks, check out `{prfx}mode`.");

            this._embed.WithFooter($"For more information on the bot in general, use '{prfx}help'");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        try
        {
            var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel.Id);
            if (existingFmCooldown.HasValue)
            {
                var msg = this.Context.Message as SocketUserMessage;
                if (StackCooldownTarget.Contains(this.Context.Message.Author))
                {
                    if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                    {
                        var secondsLeft = (int)(StackCooldownTimer[
                                StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                            .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                        if (secondsLeft <= existingFmCooldown.Value - 2)
                        {
                            _ = this.Interactivity.DelayedDeleteMessageAsync(
                                await this.Context.Channel.SendMessageAsync($"This channel has a `{existingFmCooldown.Value}` second cooldown on `{prfx}fm`. Please wait for this to expire before using this command again."),
                                TimeSpan.FromSeconds(6));
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
            }

            _ = this.Context.Channel.TriggerTypingAsync();
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var response = await this._playBuilder.NowPlayingAsync(new ContextModel(this.Context, prfx, contextUser), userSettings);

            IUserMessage message;
            if (response.ResponseType == ResponseType.Embed)
            {
                message = await ReplyAsync("", false, response.Embed.Build(), allowedMentions: AllowedMentions.None);
            }
            else
            {
                message = await ReplyAsync(response.Text, allowedMentions: AllowedMentions.None);
            }

            PublicProperties.UsedCommandsResponseMessageId.TryAdd(this.Context.Message.Id, message.Id);
            PublicProperties.UsedCommandsResponseContextId.TryAdd(message.Id, this.Context.Message.Id);

            try
            {
                if (message != null && response.CommandResponse == CommandResponse.Ok)
                {
                    if (contextUser.EmoteReactions != null && contextUser.EmoteReactions.Any() && SupporterService.IsSupporter(contextUser.UserType))
                    {
                        await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
                    }
                    else if (this.Context.Guild != null)
                    {
                        await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild);
                    }
                }
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                _ = this.Interactivity.DelayedDeleteMessageAsync(
                    await ReplyAsync(
                        $"Could not add automatic emoji reactions to `{prfx}fm`. Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`.",
                        allowedMentions: AllowedMentions.None),
                    TimeSpan.FromSeconds(60));
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("recent", RunMode = RunMode.Async)]
    [Summary("Shows you or someone else their recent tracks")]
    [Options(Constants.UserMentionExample)]
    [Examples("recent", "r", "recent @user", "recent lfm:fm-bot")]
    [Alias("recenttracks", "recents", "r", "rc")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task RecentAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = await this._playBuilder.RecentAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, userSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("overview", RunMode = RunMode.Async)]
    [Summary("Shows a daily overview")]
    [Options("Amount of days to show (max 8)")]
    [Examples("o", "overview", "overview 7")]
    [Alias("o", "ov")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    public async Task OverviewAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var amountOfDays = SettingService.GetAmount(extraOptions, 4, 8);

        try
        {
            var response = await this._playBuilder.OverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, amountOfDays);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("year", RunMode = RunMode.Async)]
    [Summary("Shows an overview of your year")]
    [Alias("yr", "lastyear", "yearoverview", "yearov", "yov", "last.year", "wrapped")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    public async Task YearAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var year = SettingService.GetYear(extraOptions).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);

        try
        {
            var response = await this._playBuilder.YearAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, year);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("pace", RunMode = RunMode.Async)]
    [Summary("Shows estimated date you reach a scrobble goal based on average scrobbles per day")]
    [Options(Constants.CompactTimePeriodList, "Optional goal amount: For example `10000` or `10k`", Constants.UserMentionExample)]
    [Examples("pc", "pc 100k q", "pc 40000 h @user", "pace", "pace yearly @user 250000")]
    [UsernameSetRequired]
    [Alias("pc")]
    [CommandCategories(CommandCategory.Other)]
    public async Task PaceAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);

        var goalAmount = SettingService.GetGoalAmount(extraOptions, userInfo.Playcount);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, TimePeriod.AllTime, timeZone: userSettings.TimeZone);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (string.IsNullOrWhiteSpace(extraOptions) &&
            !string.IsNullOrWhiteSpace(this.Context.Message.ReferencedMessage?.Content))
        {
            goalAmount = SettingService.GetGoalAmount(this.Context.Message.ReferencedMessage.Content, userInfo.Playcount);
        }

        var response = await this._playBuilder.PaceAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, timeSettings, goalAmount, userInfo.Playcount, userInfo.RegisteredUnix);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("milestone", RunMode = RunMode.Async)]
    [Summary("Shows a milestone scrobble")]
    [Options("Optional milestone amount: For example `10000` or `10k`", Constants.UserMentionExample)]
    [Examples("ms", "ms 10k", "milestone 500 @user", "milestone", "milestone @user 250k")]
    [UsernameSetRequired]
    [Alias("m", "ms")]
    [CommandCategories(CommandCategory.Other)]
    public async Task MilestoneAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var mileStoneAmount = SettingService.GetMilestoneAmount(extraOptions, userInfo.Playcount);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            if (string.IsNullOrWhiteSpace(extraOptions) &&
                !string.IsNullOrWhiteSpace(this.Context.Message.ReferencedMessage?.Content))
            {
                mileStoneAmount = SettingService.GetMilestoneAmount(this.Context.Message.ReferencedMessage.Content, userInfo.Playcount);
            }

            var response = await this._playBuilder.MileStoneAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, mileStoneAmount.amount, userInfo.Playcount, mileStoneAmount.isRandom);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("plays", RunMode = RunMode.Async)]
    [Summary("Shows your total scrobblecount for a specific time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("p", "plays", "plays @frikandel", "plays monthly")]
    [Alias("p", "scrobbles")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task PlaysAsync([Remainder] string extraOptions = null)
    {
        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context, true);
        var timeSettings = SettingService.GetTimePeriod(userSettings.NewSearchValue, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

        var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

        if (count == null)
        {
            await this.Context.Channel.SendMessageAsync($"Could not find total count for Last.fm user `{StringExtensions.Sanitize(userSettings.UserNameLastFm)}`.", allowedMentions: AllowedMentions.None);
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var userTitle = $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}";

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            await this.Context.Channel.SendMessageAsync($"**{userTitle}** has `{count}` total scrobbles", allowedMentions: AllowedMentions.None);
        }
        else
        {
            await this.Context.Channel.SendMessageAsync($"**{userTitle}** has `{count}` scrobbles in the {timeSettings.AltDescription}", allowedMentions: AllowedMentions.None);
        }
        this.Context.LogCommandUsed();
    }

    [Command("streak", RunMode = RunMode.Async)]
    [Summary("Shows you or someone else their streak")]
    [UsernameSetRequired]
    [Alias("str", "combo", "cb")]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Artists, CommandCategory.Tracks)]
    public async Task StreakAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.LastIndexed == null)
        {
            await this._indexService.IndexUser(contextUser);
        }

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var userWithStreak = await this._userService.GetUserAsync(userSettings.DiscordUserId);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._playBuilder.StreakAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, userWithStreak);

            var responseId = await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            if (response.FileName != null)
            {
                
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("streaks", RunMode = RunMode.Async)]
    [Summary("Shows you your past streaks")]
    [UsernameSetRequired]
    [Alias("strs", "combos", "cbs", "streakhistory", "combohistory", "combolist", "streaklist")]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Artists, CommandCategory.Tracks)]
    public async Task StreakHistoryAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._playBuilder.StreakHistoryAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, artist: userSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("playleaderboard", RunMode = RunMode.Async)]
    [Summary("Shows users with the most plays in your server")]
    [Alias("sblb", "scrobblelb", "scrobbleleaderboard", "scrobble leaderboard")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task PlayLeaderboardAsync([Remainder] string options = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                guild, GuildViewType.Plays);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("timeleaderboard", RunMode = RunMode.Async)]
    [Summary("Shows users with the most playtime in your server")]
    [Alias("playtimeleaderboard", "listeningtimeleaderboard", "ptlb", "ltlb", "tlb", "sleepscrobblers")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task TimeLeaderboardAsync([Remainder] string options = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                guild, GuildViewType.ListeningTime);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
