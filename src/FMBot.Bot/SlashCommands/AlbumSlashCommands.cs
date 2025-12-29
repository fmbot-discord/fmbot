using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.SlashCommands;

public class AlbumSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly SettingService _settingService;
    private readonly AlbumService _albumService;

    private InteractiveService Interactivity { get; }

    public AlbumSlashCommands(UserService userService, SettingService settingService, AlbumBuilders albumBuilders, InteractiveService interactivity, AlbumService albumService)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._albumBuilders = albumBuilders;
        this.Interactivity = interactivity;
        this._albumService = albumService;
    }

    [SlashCommand("album", "Album info for the album you're currently listening to or searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))] string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Role-picker", Description = "Display a rolepicker to filter with roles")] bool displayRoleFilter = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._albumBuilders.WhoKnowsAlbumAsync(new ContextModel(this.Context, contextUser), mode.Value, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwkalbum", "Who of your friends know an album", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task FriendsWhoKnowAlbumAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._albumBuilders.FriendsWhoKnowAlbumAsync(new ContextModel(this.Context, contextUser), mode.Value, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("gwkalbum", "Shows what other users listen to an album globally in .fmbot", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task GlobalWhoKnowsAlbumAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Hide-private", Description = "Hide or show private users")] bool hidePrivate = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name,
            ResponseMode = mode ?? contextUser.Mode ?? ResponseMode.Embed
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

    [SlashCommand("cover", "Cover for current album or the one you're searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumCoverAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))] string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(null, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser), userSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumtracks", "Shows album info for the album you're currently listening to or searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Playcount-order", Description = "If the list should be ordered by playcount")] bool orderByPlaycount = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._albumBuilders.AlbumTracksAsync(new ContextModel(this.Context, contextUser), userSettings, name, orderByPlaycount);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumplays", "Shows playcount for current album or the one you're searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumPlaysAsync(
        [SlashCommandParameter(Name = "Album", Description = "The album your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(AlbumAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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
