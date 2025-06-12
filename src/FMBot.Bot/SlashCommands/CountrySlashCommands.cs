using System.Threading.Tasks;
using System;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;
using Discord;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class CountrySlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly SettingService _settingService;

    private readonly CountryBuilders _countryBuilders;

    private InteractiveService Interactivity { get; }


    public CountrySlashCommands(UserService userService, CountryBuilders countryBuilders, GuildService guildService, InteractiveService interactivity, SettingService settingService)
    {
        this._userService = userService;
        this._countryBuilders = countryBuilders;
        this._guildService = guildService;
        this.Interactivity = interactivity;
        this._settingService = settingService;
    }

    [SlashCommand("country", "Country for artist or top artists for country")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task CountryAsync(
        [Summary("search", "The country or artist you want to view")]
        [Autocomplete(typeof(CountryArtistAutoComplete))]
        string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._countryBuilders.CountryAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("countrychart", "Generates a map of the location from your top artists")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task CountryChartAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
            defaultTimePeriod: TimePeriod.AllTime);

        var response = await this._countryBuilders.GetTopCountryChart(new ContextModel(this.Context, contextUser), userSettings, timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
