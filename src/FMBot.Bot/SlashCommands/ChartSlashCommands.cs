using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

[Group("chart", "Generate charts with album covers or artist images")]
[CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
[IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
public class ChartSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ChartBuilders _chartBuilders;
    private readonly SettingService _settingService;

    private InteractiveService Interactivity { get; }

    public ChartSlashCommands(UserService userService, SettingService settingService, InteractiveService interactivity, ChartBuilders chartBuilders)
    {
        this._userService = userService;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._chartBuilders = chartBuilders;
    }

    [SlashCommand("albums", "Generates an album image chart")]
    [UsernameSetRequired]
    public async Task AlbumChartAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Released", "Filter to albums released in year")][Autocomplete(typeof(YearAutoComplete))] string year = null,
        [Summary("Decade", "Filter to albums released in decade")][Autocomplete(typeof(DecadeAutoComplete))] string decade = null,
        [Summary("Size", "Chart size")][Autocomplete(typeof(ChartSizeAutoComplete))] string size = "3x3",
        [Summary("Titles", "Title display setting")] TitleSetting titleSetting = TitleSetting.Titles,
        [Summary("Skip", "Skip albums without an image")] bool skip = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Sfw", "Safe for work images only")] bool sfwOnly = false,
        [Summary("Rainbow", "Experimental rainbow setting")] bool rainbow = false,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var timeSettings = SettingService.GetTimePeriod(timePeriod, !string.IsNullOrWhiteSpace(year) || !string.IsNullOrWhiteSpace(decade) ? TimePeriod.AllTime : TimePeriod.Weekly,  timeZone: userSettings.TimeZone);

        var chartSettings = new ChartSettings(this.Context.User)
        {
            ArtistChart = false,
            TitleSetting = titleSetting,
            SkipWithoutImage = skip || rainbow,
            SkipNsfw = sfwOnly,
            RainbowSortingEnabled = rainbow,
            TimeSettings = timeSettings,
            TimespanString = timeSettings.Description,
            TimespanUrlString = timeSettings.UrlParameter,
            ReleaseYearFilter = !string.IsNullOrWhiteSpace(year) ? int.Parse(year) : null,
            ReleaseDecadeFilter = !string.IsNullOrWhiteSpace(decade) ? int.Parse(decade) : null,
            CustomOptionsEnabled = titleSetting != TitleSetting.Titles || skip || sfwOnly || rainbow
        };

        chartSettings = ChartService.GetDimensions(chartSettings, size);

        try
        {
            var response = await this._chartBuilders.AlbumChartAsync(new ContextModel(this.Context, contextUser), userSettings,
            chartSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("artists", "Generates an artist image chart")]
    [UsernameSetRequired]
    public async Task ArtistChartAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Size", "Chart size")][Autocomplete(typeof(ChartSizeAutoComplete))] string size = "3x3",
        [Summary("Titles", "Title display setting")] TitleSetting titleSetting = TitleSetting.Titles,
        [Summary("Skip", "Skip albums without an image")] bool skip = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Rainbow", "Experimental rainbow setting")] bool rainbow = false,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var chartSettings = new ChartSettings(this.Context.User)
        {
            ArtistChart = true,
            TitleSetting = titleSetting,
            SkipWithoutImage = skip || rainbow,
            RainbowSortingEnabled = rainbow,
            TimeSettings = timeSettings,
            TimespanString = timeSettings.Description,
            TimespanUrlString = timeSettings.UrlParameter,
            CustomOptionsEnabled = titleSetting != TitleSetting.Titles || skip || rainbow
        };

        chartSettings = ChartService.GetDimensions(chartSettings, size);

        try
        {
            var response = await this._chartBuilders.ArtistChartAsync(new ContextModel(this.Context, contextUser), userSettings,
                chartSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
