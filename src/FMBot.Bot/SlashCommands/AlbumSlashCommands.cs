using System;
using System.Collections.Generic;
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
using FMBot.Domain;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class AlbumSlashCommands : InteractionModuleBase
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

    [SlashCommand("album", "Shows album info for the album you're currently listening to or searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
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

    [ComponentInteraction($"{InteractionConstants.Album.Info}-*-*-*")]
    [UsernameSetRequired]
    public async Task AlbumAsync(string album, string discordUser, string requesterDiscordUser)
    {
        _ = DeferAsync();
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.AlbumAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), $"{dbAlbum.ArtistName} | {dbAlbum.Name}", userSettings);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
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
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Role-picker", "Display a rolepicker to filter with roles")] bool displayRoleFilter = false)
    {
        _ = DeferAsync();

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

    [ComponentInteraction($"{InteractionConstants.WhoKnowsAlbumRolePicker}-*")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string albumId, string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var album = await this._albumService.GetAlbumForId(int.Parse(albumId));

        var roleIds = new List<ulong>();
        if (inputs != null)
        {
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }
        }

        try
        {
            var response = await this._albumBuilders.WhoKnowsAlbumAsync(new ContextModel(this.Context, contextUser), ResponseMode.Embed, $"{album.ArtistName} | {album.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwkalbum", "Shows who of your friends listen to an album")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task FriendsWhoKnowAlbumAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

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

    [SlashCommand("gwkalbum", "Shows what other users listen to an album globally in .fmbot")]
    [UsernameSetRequired]
    public async Task GlobalWhoKnowsAlbumAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
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

    [SlashCommand("cover", "Cover for current album or the one you're searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task AlbumCoverAsync(
        [Summary("Album", "The album your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(AlbumAutoComplete))] string name = null)
    {
        _ = DeferAsync();

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

    [ComponentInteraction($"{InteractionConstants.Album.Cover}-*-*-*")]
    [UsernameSetRequired]
    public async Task AlbumCoverAsync(string album, string discordUser, string requesterDiscordUser)
    {
        _ = DeferAsync();
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}");

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Album.RandomCover}-*-*")]
    [UsernameSetRequired]
    public async Task RandomAlbumCoverAsync(string discordUser, string requesterDiscordUser)
    {
        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        if (this.Context.User.Id != requesterDiscordUserId)
        {
            await RespondAsync("🎲 Sorry, only the user that requested the random cover can reroll.", ephemeral: true);
            return;
        }

        _ = DeferAsync();
        await this.Context.DisableInteractionButtons();

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, "random");

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
            if (message != null && response.ReferencedMusic != null && PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
            {
                await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumtracks", "Shows album info for the album you're currently listening to or searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
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

    [ComponentInteraction($"{InteractionConstants.Album.Tracks}-*-*-*")]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(string album, string discordUser, string requesterDiscordUser)
    {
        _ = DeferAsync();
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.AlbumTracksAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}");

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("albumplays", "Shows playcount for current album or the one you're searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
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
