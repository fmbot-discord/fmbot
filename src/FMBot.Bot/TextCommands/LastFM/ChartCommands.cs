using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Charts")]
public class ChartCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly ChartService _chartService;
    private readonly IPrefixService _prefixService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly ChartBuilders _chartBuilders;

    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();

    public ChartCommands(
        GuildService guildService,
        ChartService chartService,
        IPrefixService prefixService,
        SettingService settingService,
        UserService userService,
        IOptions<BotSettings> botSettings, ChartBuilders chartBuilders, InteractiveService interactivity) : base(botSettings)
    {
        this._chartService = chartService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this._chartBuilders = chartBuilders;
        this.Interactivity = interactivity;
    }

    [Command("chart", RunMode = RunMode.Async)]
    [Summary("Generates an album image chart.")]
    [Options(
        Constants.CompactTimePeriodList,
        "Disable titles: `notitles` / `nt`",
        "Skip albums with no image: `skipemptyimages` / `s`",
        "Skip NSFW albums: `sfw`",
        "Size: `WidthxHeight` - `2x2`, `3x3`, `4x5`, `20x4` up to 100 total images",
        Constants.UserMentionExample)]
    [Examples("c", "c q 8x8 nt s", "chart 8x8 quarterly notitles skip", "c 10x10 alltime notitles skip", "c @user 7x7 yearly")]
    [Alias("c", "aoty")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Charts, CommandCategory.Albums)]
    public async Task ChartAsync(params string[] otherSettings)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var msg = this.Context.Message as SocketUserMessage;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(5) >= DateTimeOffset.Now)
            {
                var secondsLeft = (int)(StackCooldownTimer[
                        StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                    .AddSeconds(6) - DateTimeOffset.Now).TotalSeconds;
                if (secondsLeft <= 2)
                {
                    var secondString = secondsLeft == 1 ? "second" : "seconds";
                    await ReplyAsync($"Please wait {secondsLeft} {secondString} before generating a chart again.");
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

        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        var optionsAsString = "";
        if (otherSettings != null && otherSettings.Any())
        {
            optionsAsString = string.Join(" ", otherSettings);
        }
        var userSettings = await this._settingService.GetUser(optionsAsString, user, this.Context);

        if (!this._guildService.CheckIfDM(this.Context))
        {
            var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
            if (!perms.AttachFiles)
            {
                await ReplyAsync(
                    "I'm missing the 'Attach files' permission in this server, so I can't post a chart.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var chartSettings = new ChartSettings(this.Context.User)
            {
                ArtistChart = false
            };

            chartSettings = this._chartService.SetSettings(chartSettings, otherSettings);

            var response = await this._chartBuilders.AlbumChartAsync(new ContextModel(this.Context, prfx, user), userSettings,
                chartSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("artistchart", RunMode = RunMode.Async)]
    [Summary("Generates an artist image chart.")]
    [Options(
        Constants.CompactTimePeriodList,
        "Disable titles: `notitles` / `nt`",
        "Skip albums with no image: `skipemptyimages` / `s`",
        "Size: WidthxHeight - `2x2`, `3x3`, `4x5` up to `10x10`",
        Constants.UserMentionExample)]
    [Examples("ac", "ac q 8x8 nt s", "artistchart 8x8 quarterly notitles skip", "ac 10x10 alltime notitles skip", "ac @user 7x7 yearly")]
    [Alias("ac", "top")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Charts, CommandCategory.Artists)]
    public async Task ArtistChartAsync(params string[] otherSettings)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var msg = this.Context.Message as SocketUserMessage;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(5) >= DateTimeOffset.Now)
            {
                var secondsLeft = (int)(StackCooldownTimer[
                        StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                    .AddSeconds(6) - DateTimeOffset.Now).TotalSeconds;
                if (secondsLeft <= 2)
                {
                    var secondString = secondsLeft == 1 ? "second" : "seconds";
                    await ReplyAsync($"Please wait {secondsLeft} {secondString} before generating a chart again.");
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

        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        var optionsAsString = "";
        if (otherSettings != null && otherSettings.Any())
        {
            optionsAsString = string.Join(" ", otherSettings);
        }
        var userSettings = await this._settingService.GetUser(optionsAsString, user, this.Context);

        if (!this._guildService.CheckIfDM(this.Context))
        {
            var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
            if (!perms.AttachFiles)
            {
                await ReplyAsync(
                    "I'm missing the 'Attach files' permission in this server, so I can't post an artist chart.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var chartSettings = new ChartSettings(this.Context.User) { ArtistChart = true };

            chartSettings = this._chartService.SetSettings(chartSettings, otherSettings);

            var response = await this._chartBuilders.ArtistChartAsync(new ContextModel(this.Context, prfx, user), userSettings,
                chartSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
