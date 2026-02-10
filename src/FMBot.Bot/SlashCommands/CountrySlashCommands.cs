using System.Threading.Tasks;
using System;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.SlashCommands;

public class CountrySlashCommands(
    UserService userService,
    CountryBuilders countryBuilders,
    InteractiveService interactivity,
    SettingService settingService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;


    [SlashCommand("country", "Country for artist or top artists for country",
        Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task CountryAsync(
        [SlashCommandParameter(Name = "search", Description = "The country or artist you want to view",
            AutocompleteProviderType = typeof(CountryArtistAutoComplete))]
        string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await countryBuilders.CountryAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("countrychart", "Generates a map of the locations of your top artists",
        Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task CountryChartAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
            defaultTimePeriod: TimePeriod.AllTime);

        var response = await countryBuilders.GetTopCountryChart(new ContextModel(this.Context, contextUser), userSettings, timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
