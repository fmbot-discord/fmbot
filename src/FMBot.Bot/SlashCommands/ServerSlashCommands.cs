using System;
using System.Threading.Tasks;
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

namespace FMBot.Bot.SlashCommands;

[Group("server", "Server billboard commands")]
public class ServerSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly TrackBuilders _trackBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly GuildService _guildService;

    private InteractiveService Interactivity { get; }

    public ServerSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        InteractiveService interactivity,
        SettingService settingService,
        GuildService guildService,
        AlbumBuilders albumBuilders,
        TrackBuilders trackBuilders,
        GenreBuilders genreBuilders)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this.Interactivity = interactivity;
        this._guildService = guildService;
        this._albumBuilders = albumBuilders;
        this._trackBuilders = trackBuilders;
        this._genreBuilders = genreBuilders;
    }

    [SlashCommand("artists", "Top artists for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildArtistsAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period")] PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "Order", Description = "Order for chart (defaults to listeners)")] OrderType orderType = OrderType.Listeners)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
        };

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var response = await this._artistBuilders.GuildArtistsAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);

        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("albums", "Top albums for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildAlbumsAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period")] PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "Order", Description = "Order for chart (defaults to listeners)")] OrderType orderType = OrderType.Listeners,
        [SlashCommandParameter(Name = "Artist", Description = "The artist you want to filter on", AutocompleteProviderType = typeof(ArtistAutoComplete))]string artist = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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
            guildListSettings.NewSearchValue = artist;
        }

        var response = await this._albumBuilders.GuildAlbumsAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);

        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("tracks", "Top tracks for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildTracksAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period")] PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "Order", Description = "Order for chart (defaults to listeners)")] OrderType orderType = OrderType.Listeners,
        [SlashCommandParameter(Name = "Artist", Description = "The artist you want to filter on", AutocompleteProviderType = typeof(ArtistAutoComplete))]string artist = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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
            guildListSettings.NewSearchValue = artist;
        }

        var response = await this._trackBuilders.GuildTracksAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);

        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("genres", "Top genres for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildGenresAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period")] PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "Order", Description = "Order for chart (defaults to listeners)")] OrderType orderType = OrderType.Listeners)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
        };

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var response = await this._genreBuilders.GetGuildGenres(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);

        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
