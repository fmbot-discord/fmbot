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
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.SlashCommands;

public class ArtistSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;

    private InteractiveService Interactivity { get; }

    public ArtistSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        InteractiveService interactivity,
        LastFmRepository lastFmRepository,
        GuildService guildService)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
    }

    [SlashCommand("artist", "Shows artist info for the artist you're currently listening to or searching for")]
    [UsernameSetRequired]
    public async Task ArtistAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._artistBuilders.ArtistAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your artist on Last.fm due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }


    [SlashCommand("artisttracks", "Shows your top tracks for an artist")]
    [UsernameSetRequired]
    public async Task ArtistTracksAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))]string name = null,
        [Summary("Time-period", "Time period to base show tracks for")] PlayTimePeriod timePeriod = PlayTimePeriod.AllTime,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, contextUser), timeSettings,
            userSettings, name);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("artistpace", "Shows estimated date you reach a certain amount of plays on an artist")]
    [UsernameSetRequired]
    public async Task ArtistPaceAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))]string name = null,
        [Summary("Amount", "Goal play amount")] int amount = 1,
        [Summary("Time-period", "Time period to base average playcount on")] ArtistPaceTimePeriod timePeriod = ArtistPaceTimePeriod.Monthly,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(ArtistPaceTimePeriod), timePeriod), TimePeriod.Monthly);

            long timeFrom;
            if (timeSettings.TimePeriod != TimePeriod.AllTime && timeSettings.PlayDays != null)
            {
                var dateAgo = DateTime.UtcNow.AddDays(-timeSettings.PlayDays.Value);
                timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
            }
            else
            {
                timeFrom = userInfo.Registered.Unixtime;
            }

            var response = await this._artistBuilders.ArtistPaceAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, amount.ToString(), timeFrom, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your pace due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    public enum ArtistPaceTimePeriod
    {
        Weekly = 1,
        Monthly = 2
    }

    [SlashCommand("whoknows", "Shows what other users listen to an artist in your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);

        try
        {
            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser),
                guild, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your artist on Last.fm due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    [SlashCommand("globalwhoknows", "Shows what other users listen to an artist in .fmbot")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GlobalWhoKnowsAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null,
        [Summary("Hide-private", "Hide or show private users")] bool hidePrivate = false)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name
        };

        try
        {
            var response = await this._artistBuilders.GlobalWhoKnowsArtistAsync(new ContextModel(this.Context, contextUser),
                guild, currentSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your artist on Last.fm due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    [SlashCommand("taste", "Compares your top artists to another users top artists.")]
    [UsernameSetRequired]
    public async Task TasteAsync(
        [Summary("User", "The user to compare your taste with")] string user,
        [Summary("Time-period", "Time period")] TimePeriod timePeriod = TimePeriod.AllTime,
        [Summary("Type", "Taste view type")] TasteType tasteType = TasteType.Table)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(TimePeriod), timePeriod), TimePeriod.AllTime);

            var response = await this._artistBuilders.TasteAsync(new ContextModel(this.Context, contextUser),
                new TasteSettings { TasteType = tasteType }, timeSettings, userSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your taste due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }
}
