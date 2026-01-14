using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Countries")]
public class CountryCommands(
    IPrefixService prefixService,
    IndexService indexService,
    UserService userService,
    SettingService settingService,
    CountryBuilders countryBuilders,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("topcountries", "cl", "tc", "countrylist", "countries", "countrieslist")]
    [Summary("Shows a list of your or someone else's top artist countries over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("tc", "topcountries", "tc a lfm:fm-bot", "topcountries weekly @user")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    public async Task TopCountriesAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);

            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await countryBuilders.TopCountriesAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings, mode.mode);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("countrychart", "cc", "worldmap", "artistmap")]
    [Summary("Generates a map of the location from your top artists.")]
    public async Task CountryChartAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);

            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm,
                defaultTimePeriod: TimePeriod.AllTime,
                timeZone: userSettings.TimeZone);

            var response = await countryBuilders.GetTopCountryChart(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("country", "countries", "from")]
    [Summary("Shows country information for an artist, or top artists for a specific country")]
    [UsernameSetRequired]
    [SupportsPagination]
    public async Task CountryInfoAsync([CommandParameter(Remainder = true)] string countryOptions = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        try
        {
            var response = await countryBuilders.CountryAsync(new ContextModel(this.Context, prfx, contextUser), countryOptions);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
