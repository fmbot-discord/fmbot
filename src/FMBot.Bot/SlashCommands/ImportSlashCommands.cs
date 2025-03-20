using Discord;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using Fergun.Interactive;
using FMBot.Domain.Attributes;

namespace FMBot.Bot.SlashCommands;

public class ImportSlashCommands : InteractionModuleBase
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

            var embed = new EmbedBuilder();
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

        var embed = new EmbedBuilder();
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

        var embed = new EmbedBuilder();
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
                        await this.Context.Interaction.RespondWithModalAsync<ModifyAlbumModal>(InteractionConstants
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
            _ = this.Context.Channel.TriggerTypingAsync();
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickArtist(contextUser.UserId, modal.ArtistName);

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
            _ = this.Context.Channel.TriggerTypingAsync();
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickAlbum(
                contextUser.UserId,
                modal.ArtistName,
                modal.AlbumName);

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
            _ = this.Context.Channel.TriggerTypingAsync();
            await DeferAsync();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickTrack(
                contextUser.UserId,
                modal.ArtistName,
                modal.TrackName);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistRename}——*")]
    public async Task RenameArtistImportedPlays(string selectedArtistName)
    {
        try
        {
            var mb = new ModalBuilder()
                .WithTitle($"Editing '{selectedArtistName}' imports")
                .WithCustomId($"{InteractionConstants.ImportModify.ArtistRenameModal}——{selectedArtistName}")
                .AddTextInput("New artist name", "artist_name", placeholder: "The Beatles", value: selectedArtistName);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ModalInteraction($"{InteractionConstants.ImportModify.ArtistRenameModal}——*")]
    public async Task RenameArtist(string selectedArtistName, RenameArtistModal modal)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                selectedArtistName,
                modal.ArtistName);

            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = response.Embed.Build();
                e.Components = response.Components.Build();
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.ImportModify.ArtistRenameConfirmed}——*——*")]
    public async Task RenameArtistConfirmed(string selectedArtistName, string newArtistName)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Editing selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            await this._importService.RenameArtistImports(contextUser, selectedArtistName, newArtistName);

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                newArtistName,
                newArtistName,
                selectedArtistName);

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
    public async Task DeleteArtist(string artistName)
    {
        try
        {
            await DeferAsync();
            await EditToLoader();
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                artistName,
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
    public async Task DeleteArtistConfirmed(string artistName)
    {
        try
        {
            await DeferAsync();
            await EditToLoader("Deleting selected imports...");
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            await this._importService.DeleteArtistImports(contextUser, artistName);

            var response = await this._importBuilders.PickArtist(
                contextUser.UserId,
                artistName,
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
            e.Components = new ComponentBuilder().WithButton(text, customId: "0", emote: Emote.Parse(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary).Build();
        });
    }
}
