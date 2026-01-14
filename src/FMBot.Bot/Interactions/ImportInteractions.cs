using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class ImportInteractions(
    UserService userService,
    ImportService importService,
    IndexService indexService,
    ImportBuilders importBuilders,
    UserBuilder userBuilder,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.ImportModify.Modify)]
    public async Task SelectImportModifyPickButton(string pickedOption)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

            if (supporterRequired != null)
            {
                await this.Context.SendResponse(interactivity, supporterRequired);
                this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                return;
            }

            this.Context.LogCommandUsed();
            if (Enum.TryParse(pickedOption, out ImportModifyPick modifyPick))
            {
                switch (modifyPick)
                {
                    case ImportModifyPick.Artist:
                        await RespondAsync(InteractionCallback.Modal(
                            ModalFactory.CreateModifyArtistModal(InteractionConstants.ImportModify.PickArtistModal)));
                        break;
                    case ImportModifyPick.Album:
                        await RespondAsync(InteractionCallback.Modal(
                            ModalFactory.CreateModifyAlbumModal(InteractionConstants.ImportModify.PickAlbumModal)));
                        break;
                    case ImportModifyPick.Track:
                        await RespondAsync(InteractionCallback.Modal(
                            ModalFactory.CreateModifyTrackModal(InteractionConstants.ImportModify.PickTrackModal)));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistRename)]
    public async Task RenameArtistButton(string selectedArtistRef)
    {
        try
        {
            var selectedArtist = importService.GetImportRef(selectedArtistRef)?.Artist;
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateRenameArtistModal(
                    $"{InteractionConstants.ImportModify.ArtistRenameModal}:{selectedArtistRef}",
                    selectedArtist)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumRename)]
    public async Task RenameAlbumButton(string selectedAlbumRef)
    {
        try
        {
            var selectedAlbum = importService.GetImportRef(selectedAlbumRef);
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateRenameAlbumModal(
                    $"{InteractionConstants.ImportModify.AlbumRenameModal}:{selectedAlbumRef}",
                    selectedAlbum?.Artist,
                    selectedAlbum?.Album)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackRename)]
    public async Task RenameTrackButton(string selectedTrackRef)
    {
        try
        {
            var selectedTrack = importService.GetImportRef(selectedTrackRef);
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateRenameTrackModal(
                    $"{InteractionConstants.ImportModify.TrackRenameModal}:{selectedTrackRef}",
                    selectedTrack?.Artist,
                    selectedTrack?.Track)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickArtistModal)]
    public async Task PickArtist()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await importBuilders.PickArtist(contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator, importRef);

            await this.Context.SendFollowUpResponse(interactivity, response);
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
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(interactivity, response);
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
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistRenameModal)]
    public async Task RenameArtist(string selectedArtistRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoader();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newArtistRef =
                importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedArtistRef,
                newArtistRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumRenameModal)]
    public async Task RenameAlbum(string selectedAlbumRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var albumName = this.Context.GetModalValue("album_name");

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoader();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newAlbumRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedAlbumRef,
                newAlbumRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackRenameModal)]
    public async Task RenameTrack(string selectedTrackRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var trackName = this.Context.GetModalValue("track_name");

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoader();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newTrackRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedTrackRef,
                newTrackRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : null;
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
            e.Components = [new ActionRowProperties().WithButton(text, customId: "0",
                emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary)];
        });
    }

    [ComponentInteraction(InteractionConstants.ImportSetting)]
    [UsernameSetRequired]
    public async Task SetImport()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValue = stringMenuInteraction.Data.SelectedValues[0];

        if (Enum.TryParse(selectedValue, out DataSource dataSource))
        {
            var newUserSettings = await userService.SetDataSource(contextUser, dataSource);

            var name = newUserSettings.DataSource.GetAttribute<OptionAttribute>().Name;

            var description = new StringBuilder();
            description.AppendLine($"Import mode set to **{name}**");
            description.AppendLine();

            var embed = new EmbedProperties();
            embed.WithDescription(description +
                                  $"{EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Your stored top artist/albums/tracks are being recalculated, please wait for this to complete...");
            embed.WithColor(DiscordConstants.WarningColorOrange);

            List<ActionRowProperties> components = null;
            if (dataSource == DataSource.LastFm)
            {
                components =
                [
                    new ActionRowProperties()
                        .WithButton("Delete imported Spotify history", InteractionConstants.ImportClearSpotify,
                            style: ButtonStyle.Danger)
                        .WithButton("Delete imported Apple Music history", InteractionConstants.ImportClearAppleMusic,
                            style: ButtonStyle.Danger)
                ];
            }

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)
                .WithComponents(components)));
            this.Context.LogCommandUsed();

            await indexService.RecalculateTopLists(newUserSettings);

            embed.WithColor(DiscordConstants.SuccessColorGreen);
            embed.WithDescription(description +
                                  "✅ Your stored top artist/albums/tracks have successfully been recalculated.");
            await this.Context.Interaction.ModifyResponseAsync(msg => { msg.Embeds = [embed]; });
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClearSpotify)]
    [UsernameSetRequired]
    public async Task ClearImportSpotify()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        await importService.RemoveImportedSpotifyPlays(contextUser);

        var embed = new EmbedProperties();
        embed.WithDescription("All your imported Spotify history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportClearAppleMusic)]
    [UsernameSetRequired]
    public async Task ClearImportAppleMusic()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        await importService.RemoveImportedAppleMusicPlays(contextUser);

        var embed = new EmbedProperties();
        embed.WithDescription("All your imported Apple Music history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportManage)]
    [UsernameSetRequired]
    public async Task ImportManage()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response =
                await userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsSpotify)]
    [UsernameSetRequired]
    public async Task ImportInstructionsSpotify()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser),
                    true);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsAppleMusic)]
    [UsernameSetRequired]
    public async Task ImportInstructionsAppleMusic()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser),
                    true);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistRenameConfirmed)]
    public async Task RenameArtistConfirmed(string selectedArtistRef, string newArtistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Editing selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var selectedArtist = importService.GetImportRef(selectedArtistRef)?.Artist;
            var newArtist = importService.GetImportRef(newArtistRef)?.Artist;

            if (selectedArtist != null && newArtist != null)
            {
                await importService.RenameArtistImports(contextUser, selectedArtist, newArtist);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newArtistRef,
                newArtistRef,
                selectedArtistRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistDelete)]
    public async Task DeleteArtist(string artistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                artistRef,
                deletion: false);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistDeleteConfirmed)]
    public async Task DeleteArtistConfirmed(string artistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Deleting selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var artist = importService.GetImportRef(artistRef)?.Artist;

            if (artist != null)
            {
                await importService.DeleteArtistImports(contextUser, artist);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                artistRef,
                deletion: true);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumRenameConfirmed)]
    public async Task RenameAlbumConfirmed(string selectedAlbumRef, string newAlbumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Editing selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var selectedAlbum = importService.GetImportRef(selectedAlbumRef);
            var newAlbum = importService.GetImportRef(newAlbumRef);

            if (selectedAlbum != null && newAlbum != null)
            {
                await importService.RenameAlbumImports(contextUser, selectedAlbum.Artist, selectedAlbum.Album,
                    newAlbum.Artist, newAlbum.Album);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newAlbumRef,
                newAlbumRef,
                selectedAlbumRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumDelete)]
    public async Task DeleteAlbum(string albumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                albumRef,
                deletion: false);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumDeleteConfirmed)]
    public async Task DeleteAlbumConfirmed(string albumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Deleting selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var album = importService.GetImportRef(albumRef);

            if (album != null)
            {
                await importService.DeleteAlbumImports(contextUser, album.Artist, album.Album);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                albumRef,
                deletion: true);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackRenameConfirmed)]
    public async Task RenameTrackConfirmed(string selectedTrackRef, string newTrackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Editing selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var selectedTrack = importService.GetImportRef(selectedTrackRef);
            var newTrack = importService.GetImportRef(newTrackRef);

            if (selectedTrack != null && newTrack != null)
            {
                await importService.RenameTrackImports(contextUser, selectedTrack.Artist, selectedTrack.Track,
                    newTrack.Artist, newTrack.Track);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newTrackRef,
                newTrackRef,
                selectedTrackRef);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackDelete)]
    public async Task DeleteTrack(string trackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                trackRef,
                deletion: false);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackDeleteConfirmed)]
    public async Task DeleteTrackConfirmed(string trackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await EditToLoaderButton("Deleting selected imports...");
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var track = importService.GetImportRef(trackRef);

            if (track != null)
            {
                await importService.DeleteTrackImports(contextUser, track.Artist, track.Track);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                trackRef,
                deletion: true);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = response.Components?.Any() == true ? [response.Components] : [];
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private async Task EditToLoaderButton(string text = "Loading...")
    {
        await this.Context.Interaction.ModifyResponseAsync(e =>
        {
            e.Components = [new ActionRowProperties().WithButton(text, customId: "0",
                emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary)];
        });
    }
}
