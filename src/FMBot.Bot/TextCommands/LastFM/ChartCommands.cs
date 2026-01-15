using System;
using System.Linq;
using System.Threading.Tasks;
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
using Fergun.Interactive;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Charts")]
public class ChartCommands(
    GuildService guildService,
    ChartService chartService,
    IPrefixService prefixService,
    SettingService settingService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    ChartBuilders chartBuilders,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private readonly GuildService _guildService = guildService;

    private InteractiveService Interactivity { get; } = interactivity;

    [Command("chart", "c", "aoty", "albumsoftheyear", "albumoftheyear", "aotd", "albumsofthedecade", "albumofthedecade", "topster", "topsters")]
    [Summary("Generates an album image chart.")]
    [Options(
        Constants.CompactTimePeriodList,
        "Albums released in year: `r:2023`, `released:2023`",
        "Albums released in decade: `d:80s`, `decade:1990`",
        "Disable titles: `notitles` / `nt`",
        "Skip albums with no image: `skipemptyimages` / `s`",
        "Skip NSFW albums: `sfw`",
        "Size: `WidthxHeight` - `2x2`, `3x3`, `4x5`, `20x4` up to 100 total images",
        Constants.UserMentionExample)]
    [Examples("c", "c q 8x8 nt s", "chart 8x8 quarterly notitles skip", "c 10x10 alltime notitles skip", "c @user 7x7 yearly", "aoty 2023")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Charts, CommandCategory.Albums)]
    public async Task ChartAsync([CommandParameter(Remainder = true)] string otherSettings = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var user = await userService.GetUserSettingsAsync(this.Context.User);
        var chartCount = await userService.GetCommandExecutedAmount(user.UserId, "chart", DateTime.UtcNow.AddSeconds(-40));
        if (chartCount >= 4)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please wait a minute before generating charts again." });
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        var userSettings = await settingService.GetUser(otherSettings, user, this.Context);

        if (this.Context.Guild != null)
        {
            var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
            if (!perms.HasFlag(Permissions.AttachFiles))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                    { Content = "I'm missing the 'Attach files' permission in this server, so I can't post a chart." });
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var chartSettings = new ChartSettings(this.Context.User)
            {
                ArtistChart = false
            };

            var messageContent = this.Context.Message.Content.ToLower();
            var aoty = messageContent.Contains("aoty") || messageContent.Contains("albumsoftheyear") || messageContent.Contains("albumoftheyear");
            var aotd = messageContent.Contains("aotd") || messageContent.Contains("albumsofthedecade") || messageContent.Contains("albumofthedecade");

            chartSettings = await chartService.SetSettings(chartSettings, userSettings, aoty, aotd);

            var response = await chartBuilders.AlbumChartAsync(new ContextModel(this.Context, prfx, user), userSettings,
                chartSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("artistchart", "ac", "top")]
    [Summary("Generates an artist image chart.")]
    [Options(
        Constants.CompactTimePeriodList,
        "Disable titles: `notitles` / `nt`",
        "Skip albums with no image: `skipemptyimages` / `s`",
        "Size: WidthxHeight - `2x2`, `3x3`, `4x5` up to `10x10`",
        Constants.UserMentionExample)]
    [Examples("ac", "ac q 8x8 nt s", "artistchart 8x8 quarterly notitles skip", "ac 10x10 alltime notitles skip", "ac @user 7x7 yearly")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Charts, CommandCategory.Artists)]
    public async Task ArtistChartAsync([CommandParameter(Remainder = true)] string otherSettings = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var user = await userService.GetUserSettingsAsync(this.Context.User);
        var chartCount = await userService.GetCommandExecutedAmount(user.UserId, "artistchart", DateTime.UtcNow.AddSeconds(-45));
        if (chartCount >= 3)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please wait a minute before generating charts again." });
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        var userSettings = await settingService.GetUser(otherSettings, user, this.Context);

        if (this.Context.Guild != null)
        {
            var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
            if (!perms.HasFlag(Permissions.AttachFiles))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                    { Content = "I'm missing the 'Attach files' permission in this server, so I can't post a chart." });
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var chartSettings = new ChartSettings(this.Context.User) { ArtistChart = true };

            chartSettings = await chartService.SetSettings(chartSettings, userSettings);

            var response = await chartBuilders.ArtistChartAsync(new ContextModel(this.Context, prfx, user), userSettings,
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
