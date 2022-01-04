using System;
using System.Threading.Tasks;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class ArtistSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;

    public ArtistSlashCommands(UserService userService, ArtistBuilders artistBuilders, SettingService settingService)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
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

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your artist on Last.fm due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    //[SlashCommand("artisttracks", "Shows artist info for the artist you're currently listening to or searching for")]
    //[UsernameSetRequired]
    //public async Task ArtistTracksAsync(
    //    [Summary("Artist", "The artist your want to search for (defaults to currently playing)")] string artist = null,
    //    [Summary("Time-period", "Time period to base show tracks for")] PlayTimePeriod timePeriod = PlayTimePeriod.AllTime,
    //    [Summary("User", "The user to show (defaults to self)")] string user = null)
    //{
    //    _ = DeferAsync();

    //    var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
    //    var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

    //    var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);


    //}
}