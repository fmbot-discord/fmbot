using System;
using System.Globalization;
using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class TrackSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly TrackBuilders _trackBuilders;

    private InteractiveService Interactivity { get; }


    public TrackSlashCommands(UserService userService, SettingService settingService, TrackBuilders trackBuilders, InteractiveService interactivity)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._trackBuilders = trackBuilders;
        this.Interactivity = interactivity;
    }

    //[SlashCommand("receipt", "Shows your track receipt. Based on Receiptify.")]
    [UsernameSetRequired]
    public async Task ReceiptAsync(
        [Summary("Time-period", "Time period")] TimePeriod timePeriod = TimePeriod.Weekly,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(TimePeriod), timePeriod));

        if (timeSettings.DefaultPicked)
        {
            var monthName = DateTime.UtcNow.AddMonths(-1).ToString("MMM", CultureInfo.InvariantCulture);
            timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm);
        }

        var response = await this._trackBuilders.GetReceipt(new ContextModel(this.Context, contextUser), userSettings, timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
