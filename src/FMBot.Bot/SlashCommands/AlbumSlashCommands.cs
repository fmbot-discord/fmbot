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
using FMBot.Domain.Models;

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
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = await this._albumBuilders.AlbumAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("wkalbum", "Shows what other users listen to an album in your server")]
    [UsernameSetRequired]
    public async Task WhoKnowsAlbumAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode mode = WhoKnowsMode.Embed)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._albumBuilders.WhoKnowsAlbumAsync(new ContextModel(this.Context, contextUser), mode, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwkalbum", "Shows who of your friends listen to an album")]
    [UsernameSetRequired]
    public async Task FriendsWhoKnowAlbumAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode mode = WhoKnowsMode.Embed,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var response = await this._albumBuilders.FriendsWhoKnowAlbumAsync(new ContextModel(this.Context, contextUser), mode, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("gwkalbum", "Shows what other users listen to an album globally in .fmbot")]
    [UsernameSetRequired]
    public async Task GlobalWhoKnowsAlbumAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode mode = WhoKnowsMode.Embed,
        [Summary("Hide-private", "Hide or show private users")] bool hidePrivate = false)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name,
            WhoKnowsMode = mode
        };

        try
        {
            var response = await this._albumBuilders.GlobalWhoKnowsAlbumAsync(new ContextModel(this.Context, contextUser), currentSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("cover", "Cover for current album or the one you're searching for.")]
    [UsernameSetRequired]
    public async Task AlbumCoverAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumtracks", "Shows album info for the album you're currently listening to or searching for")]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
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
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumplays", "Shows playcount for current album or the one you're searching for.")]
    [UsernameSetRequired]
    public async Task AlbumPlaysAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._albumBuilders.AlbumPlaysAsync(new ContextModel(this.Context, contextUser), userSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
