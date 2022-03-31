using System;
using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class AlbumSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly SettingService _settingService;

    private InteractiveService Interactivity { get; }

    public AlbumSlashCommands(UserService userService, SettingService settingService, AlbumBuilders albumBuilders, InteractiveService interactivity)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._albumBuilders = albumBuilders;
        this.Interactivity = interactivity;
    }

    [SlashCommand("album", "Shows album info for the album you're currently listening to or searching for")]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [Summary("Album", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._albumBuilders.AlbumAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your album due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    [SlashCommand("albumtracks", "Shows album info for the album you're currently listening to or searching for")]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(
        [Summary("Album", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._albumBuilders.AlbumTracksAsync(new ContextModel(this.Context, contextUser), userSettings, name);

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
}
