using System;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Countries")]
public class CountryCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly IIndexService _indexService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly CountryBuilders _countryBuilders;
    private InteractiveService Interactivity { get; }

    public CountryCommands(IPrefixService prefixService,
        IIndexService indexService,
        UserService userService,
        SettingService settingService,
        CountryBuilders countryBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._indexService = indexService;
        this._userService = userService;
        this._settingService = settingService;
        this._countryBuilders = countryBuilders;
        this.Interactivity = interactivity;
    }

    [Command("topcountries", RunMode = RunMode.Async)]
    [Summary("Shows a list of your or someone else's top artist countries over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("tc", "topcountries", "tc a lfm:fm-bot", "topcountries weekly @user")]
    [Alias("cl", "tc", "countrylist", "countries", "top countries", "countrieslist")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    public async Task TopCountriesAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);

            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);

            var response = await this._countryBuilders.GetTopCountries(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("countrychart", RunMode = RunMode.Async)]
    [Summary("Generates a map of the location from your top artists.")]
    [Alias("cc")]
    public async Task TestAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);

            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm, defaultTimePeriod: TimePeriod.AllTime);

            var response = await this._countryBuilders.GetTopCountryChart(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("country", RunMode = RunMode.Async)]
    [Summary("Shows country information for an artist, or top artists for a specific country")]
    [Alias("countries", "from")]
    [UsernameSetRequired]
    [SupportsPagination]
    public async Task CountryInfoAsync([Remainder] string countryOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var response = await this._countryBuilders.CountryAsync(new ContextModel(this.Context, prfx, contextUser), countryOptions);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
