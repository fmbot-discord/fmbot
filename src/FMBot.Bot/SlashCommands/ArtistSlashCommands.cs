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
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.SlashCommands;

public class ArtistSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly LastFmRepository _lastFmRepository;

    private InteractiveService Interactivity { get; }

    public ArtistSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        InteractiveService interactivity,
        LastFmRepository lastFmRepository)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._lastFmRepository = lastFmRepository;
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
            var response = await this._artistBuilders.ArtistAsync("/", this.Context.Guild, this.Context.User,
                contextUser, name);

            await FollowupAsync(null, new[] { response.Embed.Build() });

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

        var response = await this._artistBuilders.ArtistTracksAsync(this.Context.Guild, this.Context.User, contextUser, timeSettings,
            userSettings, name);

        if (response.ResponseType == ResponseType.Embed)
        {
            await RespondAsync(null, new[] { response.Embed.Build() });
        }
        else
        {
            _ = this.Interactivity.SendPaginatorAsync(
                response.StaticPaginator,
                (SocketInteraction)this.Context.Interaction,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
        }

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
            var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm);
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

            var response = await this._artistBuilders.ArtistPaceAsync(this.Context.User, contextUser,
                userSettings, timeSettings, amount.ToString(), timeFrom, name);

            await FollowupAsync(response.Text, allowedMentions: AllowedMentions.None);

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
}
