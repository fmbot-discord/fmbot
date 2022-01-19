using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class ServerSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly GuildService _guildService;

    private InteractiveService Interactivity { get; }

    public ServerSlashCommands(UserService userService, ArtistBuilders artistBuilders, InteractiveService interactivity, SettingService settingService, GuildService guildService)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this.Interactivity = interactivity;
        this._settingService = settingService;
        this._guildService = guildService;
    }

    [SlashCommand("serverartists", "Top artists for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task ArtistTracksAsync(
        [Summary("Time-period", "Time period for chart")] PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [Summary("Order", "Order for chart (defaults to listeners)")] OrderType orderType = OrderType.Listeners,
        [Summary("Artist", "The artist you want to filter on")]
        [Autocomplete(typeof(ArtistAutoComplete))]string artist = null)
    {
        await DeferAsync();

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
            NewSearchValue = artist
        };

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var response = await this._artistBuilders.GuildArtistsAsync("/", this.Context.Guild, guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);

        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
