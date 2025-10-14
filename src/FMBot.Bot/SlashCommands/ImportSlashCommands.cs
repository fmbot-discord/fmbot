
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.SlashCommands;

public class ImportSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly ImportService _importService;
    private readonly IndexService _indexService;
    private readonly ImportBuilders _importBuilders;
    private readonly UserBuilder _userBuilder;
    private InteractiveService Interactivity { get; }

    public ImportSlashCommands(UserService userService,
        ImportService importService,
        IndexService indexService,
        InteractiveService interactivity,
        ImportBuilders importBuilders,
        UserBuilder userBuilder)
    {
        this._userService = userService;
        this._importService = importService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._importBuilders = importBuilders;
        this._userBuilder = userBuilder;
    }

    [ComponentInteraction(InteractionConstants.ImportSetting)]
    [UsernameSetRequired]
    public async Task SetImport(string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out DataSource dataSource))
        {
            var newUserSettings = await this._userService.SetDataSource(contextUser, dataSource);

            var name = newUserSettings.DataSource.GetAttribute<OptionAttribute>().Name;

            var description = new StringBuilder();
            description.AppendLine($"Import mode set to **{name}**");
            description.AppendLine();

            var embed = new EmbedProperties();
            embed.WithDescription(description +
                                  $"{DiscordConstants.Loading} Your stored top artist/albums/tracks are being recalculated, please wait for this to complete...");
            embed.WithColor(DiscordConstants.WarningColorOrange);

            ComponentBuilder components = null;
            if (dataSource == DataSource.LastFm)
            {
                components = new ComponentBuilder()
                    .WithButton("Delete imported Spotify history", InteractionConstants.ImportClearSpotify,
                        style: ButtonStyle.Danger, row: 0)
                    .WithButton("Delete imported Apple Music history", InteractionConstants.ImportClearAppleMusic,
                        style: ButtonStyle.Danger, row: 0);
            }

            await this.Context.Interaction.RespondAsync(null, [embed.Build()], ephemeral: true,
                components: components?.Build());
            this.Context.LogCommandUsed();

            await this._indexService.RecalculateTopLists(newUserSettings);

            embed.WithColor(DiscordConstants.SuccessColorGreen);
            embed.WithDescription(description +
                                  "✅ Your stored top artist/albums/tracks have successfully been recalculated.");
            await this.Context.Interaction.ModifyOriginalResponseAsync(msg => { msg.Embed = embed.Build(); });
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClearSpotify)]
    [UsernameSetRequired]
    public async Task ClearImportSpotify()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        await this._importService.RemoveImportedSpotifyPlays(contextUser);

        var embed = new EmbedProperties();
        embed.WithDescription($"All your imported Spotify history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportClearAppleMusic)]
    [UsernameSetRequired]
    public async Task ClearImportAppleMusic()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        await this._importService.RemoveImportedAppleMusicPlays(contextUser);

        var embed = new EmbedProperties();
        embed.WithDescription($"All your imported Apple Music history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportManage)]
    [UsernameSetRequired]
    public async Task ImportManage()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        await DeferAsync(true);

        try
        {
            var response =
                await this._userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
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
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser),
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
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser),
                    true);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.Modify}-*")]
    [UsernameSetRequired]
    public async Task ModifyImport(string pickedOption)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

            if (supporterRequired != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequired);
                this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                return;
            }

            this.Context.LogCommandUsed();
            if (Enum.TryParse(pickedOption, out ImportModifyPick modifyPick))
            {
                switch (modifyPick)
                {
                    case ImportModifyPick.Artist:
                        await this.Context.Interaction.RespondWithModalAsync<ModifyArtistModal>(InteractionConstants
                            .ImportModify.PickArtistModal);
                        break;
                    case ImportModifyPick.Album:
                        await this.Context.Interaction.RespondWithModalAsync<ModifyAlbumModal>(InteractionConstants
                            .ImportModify.PickAlbumModal);
                        break;
                    case ImportModifyPick.Track:
                        await this.Context.Interaction.RespondWithModalAsync<ModifyTrackModal>(InteractionConstants
                            .ImportModify.PickTrackModal);
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

    [ModalInteraction(InteractionConstants.ImportModify.PickArtistModal)]
    public async Task PickArtist(ModifyArtistModal modal)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic { Artist = modal.ArtistName });

            var response = await this._importBuilders.PickArtist(contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator, importRef);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ModalInteraction(InteractionConstants.ImportModify.PickAlbumModal)]
    public async Task PickAlbum(ModifyAlbumModal modal)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = modal.ArtistName, Album = modal.AlbumName });

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ModalInteraction(InteractionConstants.ImportModify.PickTrackModal)]
    public async Task PickTrack(ModifyTrackModal modal)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = modal.ArtistName, Track = modal.TrackName });

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistRename}——*")]
    public async Task RenameArtistImportedPlays(string selectedArtistRef)
    {
        try
        {
            var selectedArtist = this._importService.GetImportRef(selectedArtistRef)?.Artist;

            var mb = new ModalBuilder()
                .WithTitle($"Editing artist")
                .WithCustomId($"{InteractionConstants.ImportModify.ArtistRenameModal}——{selectedArtistRef}")
                .AddTextInput("New artist name", "artist_name", placeholder: "The Beatles", value: selectedArtist);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ModalInteraction($"{InteractionConstants.ImportModify.ArtistRenameModal}——*")]
    public async Task RenameArtist(string selectedArtistRef, RenameArtistModal modal)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newArtistRef =
                this._importService.StoreImportReference(new ReferencedMusic { Artist = modal.ArtistName });

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedArtistRef,
                newArtistRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistRenameConfirmed}——*——*")]
    public async Task RenameArtistConfirmed(string selectedArtistRef, string newArtistRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Editing selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var selectedArtist = this._importService.GetImportRef(selectedArtistRef)?.Artist;
            var newArtist = this._importService.GetImportRef(newArtistRef)?.Artist;

            if (selectedArtist != null && newArtist != null)
            {
                await this._importService.RenameArtistImports(contextUser, selectedArtist, newArtist);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newArtistRef,
                newArtistRef,
                selectedArtistRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistDelete}——*")]
    public async Task DeleteArtist(string artistRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                artistRef,
                deletion: false);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistDeleteConfirmed}——*")]
    public async Task DeleteArtistConfirmed(string artistRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Deleting selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var artist = this._importService.GetImportRef(artistRef)?.Artist;

            if (artist != null)
            {
                await this._importService.DeleteArtistImports(contextUser, artist);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                artistRef,
                deletion: true);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.AlbumRename}——*")]
    public async Task RenameAlbumImportedPlays(string selectedAlbumRef)
    {
        try
        {
            var selectedAlbum = this._importService.GetImportRef(selectedAlbumRef);

            var mb = new ModalBuilder()
                .WithTitle($"Editing album")
                .WithCustomId($"{InteractionConstants.ImportModify.AlbumRenameModal}——{selectedAlbumRef}")
                .AddTextInput("Artist name", "artist_name", placeholder: "The Beatles", value: selectedAlbum?.Artist)
                .AddTextInput("Album name", "album_name", placeholder: "Abbey Road", value: selectedAlbum?.Album);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ModalInteraction($"{InteractionConstants.ImportModify.AlbumRenameModal}——*")]
    public async Task RenameAlbum(string selectedAlbumRef, RenameAlbumModal modal)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newAlbumRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = modal.ArtistName, Album = modal.AlbumName });

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedAlbumRef,
                newAlbumRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.AlbumRenameConfirmed}——*——*")]
    public async Task RenameAlbumConfirmed(string selectedAlbumRef, string newAlbumRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Editing selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var selectedAlbum = this._importService.GetImportRef(selectedAlbumRef);
            var newAlbum = this._importService.GetImportRef(newAlbumRef);

            if (selectedAlbum != null && newAlbum != null)
            {
                await this._importService.RenameAlbumImports(contextUser, selectedAlbum.Artist, selectedAlbum.Album,
                    newAlbum.Artist, newAlbum.Album);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newAlbumRef,
                newAlbumRef,
                selectedAlbumRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.AlbumDelete}——*")]
    public async Task DeleteAlbum(string albumRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                albumRef,
                deletion: false);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.AlbumDeleteConfirmed}——*")]
    public async Task DeleteAlbumConfirmed(string albumRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Deleting selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var album = this._importService.GetImportRef(albumRef);

            if (album != null)
            {
                await this._importService.DeleteAlbumImports(contextUser, album.Artist, album.Album);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                albumRef,
                deletion: true);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.TrackRename}——*")]
    public async Task RenameTrackImportedPlays(string selectedTrackRef)
    {
        try
        {
            var selectedTrack = this._importService.GetImportRef(selectedTrackRef);

            var mb = new ModalBuilder()
                .WithTitle($"Editing track")
                .WithCustomId($"{InteractionConstants.ImportModify.TrackRenameModal}——{selectedTrackRef}")
                .AddTextInput("Artist name", "artist_name", placeholder: "The Beatles", value: selectedTrack?.Artist)
                .AddTextInput("Track name", "track_name", placeholder: "Yesterday", value: selectedTrack?.Track);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ModalInteraction($"{InteractionConstants.ImportModify.TrackRenameModal}——*")]
    public async Task RenameTrack(string selectedTrackRef, RenameTrackModal modal)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newTrackRef = this._importService.StoreImportReference(new ReferencedMusic
                { Artist = modal.ArtistName, Track = modal.TrackName });

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedTrackRef,
                newTrackRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.TrackRenameConfirmed}——*——*")]
    public async Task RenameTrackConfirmed(string selectedTrackRef, string newTrackRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Editing selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var selectedTrack = this._importService.GetImportRef(selectedTrackRef);
            var newTrack = this._importService.GetImportRef(newTrackRef);

            if (selectedTrack != null && newTrack != null)
            {
                await this._importService.RenameTrackImports(contextUser, selectedTrack.Artist, selectedTrack.Track,
                    newTrack.Artist, newTrack.Track);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                newTrackRef,
                newTrackRef,
                selectedTrackRef);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.TrackDelete}——*")]
    public async Task DeleteTrack(string trackRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                trackRef,
                deletion: false);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.TrackDeleteConfirmed}——*")]
    public async Task DeleteTrackConfirmed(string trackRef)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Deleting selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var track = this._importService.GetImportRef(trackRef);

            if (track != null)
            {
                await this._importService.DeleteTrackImports(contextUser, track.Artist, track.Track);
                if (contextUser.DataSource != DataSource.LastFm)
                {
                    _ = this._indexService.RecalculateTopLists(contextUser);
                }
            }

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                trackRef,
                deletion: true);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components?.Build();
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
        await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
        {
            e.Components = new ComponentBuilder().WithButton(text, customId: "0",
                emote: Emote.Parse(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary).Build();
        });
    }
}
