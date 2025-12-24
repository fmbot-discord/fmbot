using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using MetaBrainz.MusicBrainz.Interfaces;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Plays")]
public class PlayCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildBuilders _guildBuilders;
    private readonly RecapBuilders _recapBuilders;
    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();


    public PlayCommands(
        GuildService guildService,
        IndexService indexService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        SettingService settingService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        PlayBuilder playBuilder,
        GuildBuilders guildBuilders,
        RecapBuilders recapBuilders) : base(botSettings)
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
        this._recapBuilders = recapBuilders;
    }

    [Command("discoverydate", "dd", "datediscovered", "datediscovery")]
    [Summary("Shows the date you discovered the artist, album, and track")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterExclusive(
        "To see when you've discovered this artist, album and track we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task DateDiscoveredAsync([CommandParameter(Remainder = true)] string options = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response = await this._playBuilder.DiscoveryDate(context, userSettings.NewSearchValue, userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("fm", "np", "qm", "wm", "em", "rm", "tm", "ym", "om", "pm", "gm", "sm", "hm", "jm", "km",
        "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "nowplaying", "ɯɟ")]
    [Summary("Shows you or someone else's current track")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task NowPlayingAsync([CommandParameter(Remainder = true)] string options = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser?.UserNameLastFM == null)
        {
            var userNickname = (this.Context.User as SocketGuildUser)?.DisplayName;
            this._embed.UsernameNotSetErrorResponse(prfx,
                userNickname ?? this.Context.User.GlobalName ?? this.Context.User.Username);

            await ReplyAsync("", false, this._embed.Build(), components: GenericEmbedService.UsernameNotSetErrorComponents().Build());
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
                    if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)]
                            .AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                    {
                        var secondsLeft = (int)(StackCooldownTimer[
                                StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                            .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                        if (secondsLeft <= existingFmCooldown.Value - 2)
                        {
                            _ = this.Interactivity.DelayedDeleteMessageAsync(
                                await this.Context.Channel.SendMessageAsync(
                                    $"This channel has a `{existingFmCooldown.Value}` second cooldown on `{prfx}fm`. Please wait for this to expire before using this command again."),
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

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);
            var configuredFmType = SettingService.GetEmbedType(userSettings.NewSearchValue, contextUser.FmEmbedType);

            var response =
                await this._playBuilder.NowPlayingAsync(new ContextModel(this.Context, prfx, contextUser),
                    userSettings, configuredFmType.embedType);;

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("recent", "recenttracks", "recents", "r", "rc")]
    [Summary("Shows you or someone else's recent tracks")]
    [Options(Constants.UserMentionExample, "Artist name")]
    [Examples("recent", "r", "recent @user", "recent lfm:fm-bot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterEnhanced("Supporters can view their lifetime history")]
    public async Task RecentAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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

    [Command("overview", "o", "ov")]
    [Summary("Shows a daily overview")]
    [Options("Amount of days to show (max 8)")]
    [Examples("o", "overview", "overview 7")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    [SupporterEnhanced("See your lifetime history day to day as a supporter")]
    public async Task OverviewAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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

    [Command("year", "yr", "lastyear", "yearoverview", "yearov", "yov", "last.year")]
    [Summary("Shows an overview of your year")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    [SupporterEnhanced("Get an extra page with artist discoveries and a monthly overview")]
    public async Task YearAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var year = SettingService.GetYear(extraOptions).GetValueOrDefault(DateTime.UtcNow.AddDays(-30).Year);

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

    [Command("recap", "rcp", "wrapped")]
    [Summary("A recap to easily view multiple .fmbot commands into one")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    public async Task RecapAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var year = SettingService.GetYear(extraOptions).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);
        var timePeriod = !string.IsNullOrWhiteSpace(extraOptions) ? extraOptions : year.ToString();

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var timeSettings = SettingService.GetTimePeriod(timePeriod, registeredLastFm: userSettings.RegisteredLastFm,
            timeZone: userSettings.TimeZone, defaultTimePeriod: TimePeriod.Yearly);

        try
        {
            var loading = false;
            if (!this._recapBuilders.RecapCacheHot(timeSettings.Description, userSettings.UserNameLastFm))
            {
                await Context.Message.AddReactionAsync(EmojiProperties.Custom(DiscordConstants.Loading));
                loading = true;
            }

            var response = await this._recapBuilders.RecapAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, RecapPage.Overview);

            await this.Context.SendResponse(this.Interactivity, response);

            if (loading)
            {
                await Context.Message.RemoveReactionAsync(EmojiProperties.Custom(DiscordConstants.Loading),
                    this._botSettings.Discord.ApplicationId.GetValueOrDefault());
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("pace", "pc")]
    [Summary("Shows estimated date you reach a scrobble goal based on average scrobbles per day")]
    [Options(Constants.CompactTimePeriodList, "Optional goal amount: For example `10000` or `10k`",
        Constants.UserMentionExample)]
    [Examples("pc", "pc 100k q", "pc 40000 h @user", "pace", "pace yearly @user 250000")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task PaceAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);

        var goalAmount = SettingService.GetGoalAmount(extraOptions, userInfo.Playcount);
        var timeSettings =
            SettingService.GetTimePeriod(extraOptions, TimePeriod.AllTime, timeZone: userSettings.TimeZone);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (string.IsNullOrWhiteSpace(extraOptions) &&
            !string.IsNullOrWhiteSpace(this.Context.Message.ReferencedMessage?.Content))
        {
            goalAmount =
                SettingService.GetGoalAmount(this.Context.Message.ReferencedMessage.Content, userInfo.Playcount);
        }

        var response = await this._playBuilder.PaceAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, timeSettings, goalAmount, userInfo.Playcount, userInfo.RegisteredUnix);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("milestone", "m", "ms")]
    [Summary("Shows a milestone scrobble")]
    [Options("Optional milestone amount: For example `10000` or `10k`", Constants.UserMentionExample)]
    [Examples("ms", "ms 10k", "milestone 500 @user", "milestone", "milestone @user 250k")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task MilestoneAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var mileStoneAmount = SettingService.GetMilestoneAmount(extraOptions, userInfo.Playcount);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            if (string.IsNullOrWhiteSpace(extraOptions) &&
                !string.IsNullOrWhiteSpace(this.Context.Message.ReferencedMessage?.Content))
            {
                mileStoneAmount = SettingService.GetMilestoneAmount(this.Context.Message.ReferencedMessage.Content,
                    userInfo.Playcount);
            }

            var response = await this._playBuilder.MileStoneAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, mileStoneAmount.amount, userInfo.Playcount, mileStoneAmount.isRandom);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("plays", "p", "scrobbles")]
    [Summary("Shows your total scrobble count for a specific time period")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("p", "plays", "plays @frikandel", "plays monthly")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    [SupporterEnhanced($"For non-supporters, the maximum amount of cached plays is limited to their last 15000 scrobbles, meaning this is also the limit for streaks")]
    public async Task PlaysAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var timeSettings = SettingService.GetTimePeriod(userSettings.NewSearchValue, TimePeriod.AllTime,
            timeZone: userSettings.TimeZone);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._playBuilder.PlaysAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, timeSettings);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("streak", "str", "combo", "cb")]
    [Summary("Shows you or someone else's streak")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Artists, CommandCategory.Tracks)]
    [SupporterEnhanced(
        "Streaks for non-supporters are limited to 25k plays, due to the bot not caching plays beyond this limit for free users")]
    public async Task StreakAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("streaks", "strs", "combos", "cbs", "streakhistory", "combohistory", "combolist", "streaklist")]
    [Summary("Shows you your past streaks")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Artists, CommandCategory.Tracks)]
    public async Task StreakHistoryAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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

    [Command("playleaderboard", "sblb", "scrobblelb", "scrobbleleaderboard", "scrobble leaderboard")]
    [Summary("Shows users with the most plays in your server")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task PlayLeaderboardAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._guildBuilders.MemberOverviewAsync(
                new ContextModel(this.Context, prfx, contextUser),
                guild, GuildViewType.Plays);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("timeleaderboard", "playtimeleaderboard", "listeningtimeleaderboard", "ptlb", "ltlb", "tlb", "sleepscrobblers")]
    [Summary("Shows users with the most playtime in your server")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task TimeLeaderboardAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._guildBuilders.MemberOverviewAsync(
                new ContextModel(this.Context, prfx, contextUser),
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
