using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("chart", "Generate charts with album covers or artist images",
    Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
    IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
public class ChartSlashCommands(
    UserService userService,
    SettingService settingService,
    InteractiveService interactivity,
    ChartBuilders chartBuilders,
    ArtistsService artistsService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SubSlashCommand("albums", "Generates an album image chart")]
    [UsernameSetRequired]
    public async Task AlbumChartAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "artist", Description = "Filter to a specific artist", AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string artist = null,
        [SlashCommandParameter(Name = "released", Description = "Filter to albums released in year", AutocompleteProviderType = typeof(YearAutoComplete))]
        string year = null,
        [SlashCommandParameter(Name = "decade", Description = "Filter to albums released in decade", AutocompleteProviderType = typeof(DecadeAutoComplete))]
        string decade = null,
        [SlashCommandParameter(Name = "size", Description = "Chart size", AutocompleteProviderType = typeof(ChartSizeAutoComplete))]
        string size = "3x3",
        [SlashCommandParameter(Name = "titles", Description = "Title display setting")]
        TitleSetting titleSetting = TitleSetting.Titles,
        [SlashCommandParameter(Name = "skip", Description = "Skip albums without an image")]
        bool skip = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "sfw", Description = "Safe for work images only")]
        bool sfwOnly = false,
        [SlashCommandParameter(Name = "rainbow", Description = "Experimental rainbow setting")]
        bool rainbow = false,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        Artist filteredArtist = null;
        if (!string.IsNullOrWhiteSpace(artist))
        {
            filteredArtist = await artistsService.GetArtistFromDatabase(artist);
        }

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            !string.IsNullOrWhiteSpace(year) || !string.IsNullOrWhiteSpace(decade) || filteredArtist != null
                ? TimePeriod.AllTime
                : TimePeriod.Weekly,
            timeZone: userSettings.TimeZone);

        var chartSettings = new ChartSettings(this.Context.User)
        {
            ArtistChart = false,
            FilteredArtist = filteredArtist,
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

        chartSettings = ChartService.GetDimensions(chartSettings, size).newChartSettings;

        try
        {
            var response = await chartBuilders.AlbumChartAsync(new ContextModel(this.Context, contextUser), userSettings,
                chartSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SubSlashCommand("artists", "Generates an artist image chart")]
    [UsernameSetRequired]
    public async Task ArtistChartAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "size", Description = "Chart size", AutocompleteProviderType = typeof(ChartSizeAutoComplete))]
        string size = "3x3",
        [SlashCommandParameter(Name = "titles", Description = "Title display setting")]
        TitleSetting titleSetting = TitleSetting.Titles,
        [SlashCommandParameter(Name = "skip", Description = "Skip artists without an image")]
        bool skip = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "rainbow", Description = "Experimental rainbow setting")]
        bool rainbow = false,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
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

        chartSettings = ChartService.GetDimensions(chartSettings, size).newChartSettings;

        try
        {
            var response = await chartBuilders.ArtistChartAsync(new ContextModel(this.Context, contextUser), userSettings,
                chartSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
