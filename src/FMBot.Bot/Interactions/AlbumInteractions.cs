using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class AlbumInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly SettingService _settingService;
    private readonly AlbumService _albumService;
    private readonly InteractiveService _interactivity;

    public AlbumInteractions(
        UserService userService,
        AlbumBuilders albumBuilders,
        SettingService settingService,
        AlbumService albumService,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._albumBuilders = albumBuilders;
        this._settingService = settingService;
        this._albumService = albumService;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.Album.Info)]
    [UsernameSetRequired]
    public async Task AlbumAsync(string album, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.AlbumAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), $"{dbAlbum.ArtistName} | {dbAlbum.Name}", userSettings);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.WhoKnowsAlbumRolePicker)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string albumId, params string[] inputs)
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

    [ComponentInteraction(InteractionConstants.Album.Cover)]
    [UsernameSetRequired]
    public async Task AlbumCoverAsync(string album, string discordUser, string requesterDiscordUser, string type)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}", type == "motion");

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Album.RandomCover)]
    [UsernameSetRequired]
    public async Task RandomAlbumCoverAsync(string discordUser, string requesterDiscordUser)
    {
        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        if (this.Context.User.Id != requesterDiscordUserId)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("🎲 Sorry, only the user that requested the random cover can reroll.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableInteractionButtons();

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, "random");

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
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

    [ComponentInteraction(InteractionConstants.Album.Tracks)]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(string album, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await this._albumService.GetAlbumForId(albumId);

        try
        {
            var response = await this._albumBuilders.AlbumTracksAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}");

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
