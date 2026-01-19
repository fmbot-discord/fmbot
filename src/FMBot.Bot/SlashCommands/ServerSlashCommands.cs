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
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("server", "Server billboard commands")]
public class ServerSlashCommands(
    ArtistBuilders artistBuilders,
    InteractiveService interactivity,
    GuildService guildService,
    AlbumBuilders albumBuilders,
    TrackBuilders trackBuilders,
    GenreBuilders genreBuilders,
    UserService userService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SubSlashCommand("artists", "Top artists for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildArtistsAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period")]
        PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "order", Description = "Order for chart (defaults to listeners)")]
        OrderType orderType = OrderType.Listeners)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
        };

        var timeSettings =
            SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var response = await artistBuilders.GuildArtistsAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);

        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("albums", "Top albums for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildAlbumsAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period")]
        PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "order", Description = "Order for chart (defaults to listeners)")]
        OrderType orderType = OrderType.Listeners,
        [SlashCommandParameter(Name = "artist", Description = "The artist you want to filter on",
            AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string artist = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
            NewSearchValue = artist
        };

        var timeSettings =
            SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
            guildListSettings.NewSearchValue = artist;
        }

        var response = await albumBuilders.GuildAlbumsAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);

        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("tracks", "Top tracks for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildTracksAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period")]
        PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "order", Description = "Order for chart (defaults to listeners)")]
        OrderType orderType = OrderType.Listeners,
        [SlashCommandParameter(Name = "artist", Description = "The artist you want to filter on",
            AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string artist = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
            NewSearchValue = artist
        };

        var timeSettings =
            SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
            guildListSettings.NewSearchValue = artist;
        }

        var response = await trackBuilders.GuildTracksAsync(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);

        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("genres", "Top genres for your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GuildGenresAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period")]
        PlayTimePeriod timePeriod = PlayTimePeriod.Weekly,
        [SlashCommandParameter(Name = "order", Description = "Order for chart (defaults to listeners)")]
        OrderType orderType = OrderType.Listeners)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = orderType,
            AmountOfDays = 7,
        };

        var timeSettings =
            SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var response = await genreBuilders.GetGuildGenres(new ContextModel(this.Context), guild, guildListSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);

        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
