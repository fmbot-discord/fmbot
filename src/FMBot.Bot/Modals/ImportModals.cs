using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class ImportModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly ImportService _importService;
    private readonly IndexService _indexService;
    private readonly ImportBuilders _importBuilders;
    private readonly InteractiveService _interactivity;

    public ImportModals(
        UserService userService,
        ImportService importService,
        IndexService indexService,
        ImportBuilders importBuilders,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._importService = importService;
        this._indexService = indexService;
        this._importBuilders = importBuilders;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickArtistModal)]
    public async Task PickArtist()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await RespondAsync(InteractionCallback.DeferredMessage());
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await this._importBuilders.PickArtist(contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator, importRef);

            await this.Context.SendFollowUpResponse(this._interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickAlbumModal)]
    public async Task PickAlbum()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var albumName = this.Context.GetModalValue("album_name");

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await RespondAsync(InteractionCallback.DeferredMessage());
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(this._interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickTrackModal)]
    public async Task PickTrack()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var trackName = this.Context.GetModalValue("track_name");

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await RespondAsync(InteractionCallback.DeferredMessage());
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(this._interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistRenameModal}——*")]
    public async Task RenameArtist(string selectedArtistRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            await RespondAsync(InteractionCallback.DeferredMessage());
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newArtistRef =
                this._importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedArtistRef,
                newArtistRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components != null ? [response.Components] : null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.AlbumRenameModal}——*")]
    public async Task RenameAlbum(string selectedAlbumRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var albumName = this.Context.GetModalValue("album_name");

            await RespondAsync(InteractionCallback.DeferredMessage());
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newAlbumRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedAlbumRef,
                newAlbumRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components != null ? [response.Components] : null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.TrackRenameModal}——*")]
    public async Task RenameTrack(string selectedTrackRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var trackName = this.Context.GetModalValue("track_name");

            await RespondAsync(InteractionCallback.DeferredMessage());
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newTrackRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedTrackRef,
                newTrackRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components != null ? [response.Components] : null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private async Task EditToLoader(string text = "Loading...")
    {
        await this.Context.Interaction.ModifyResponseAsync(e =>
        {
            e.Embeds = [new EmbedProperties().WithDescription(text)];
            e.Components = [];
        });
    }
}
