using System.Threading.Tasks;
using System;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.SlashCommands;

public class CountrySlashCommands : ApplicationCommandModule<ApplicationCommandContext>
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

    [SlashCommand("country", "Country for artist or top artists for country", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task CountryAsync(
        [SlashCommandParameter(Name = "search", Description = "The country or artist you want to view", AutocompleteProviderType = typeof(CountryArtistAutoComplete))]
        string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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

    [SlashCommand("countrychart", "Generates a map of the location from your top artists", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task CountryChartAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

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
