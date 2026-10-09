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
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Hangfire;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Interactions;

public class ImportInteractions(
    UserService userService,
    ImportService importService,
    IndexService indexService,
    PlayService playService,
    ImportBuilders importBuilders,
    UserBuilder userBuilder,
    InteractiveService interactivity,
    IMemoryCache cache)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.ImportUpload)]
    [UsernameSetRequired]
    public async Task ImportUploadButton(string source)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

            if (supporterRequired != null)
            {
                await this.Context.SendResponse(interactivity, supporterRequired, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(supporterRequired, userService);
                return;
            }

            if (!Enum.TryParse(source, out PlaySource playSource))
            {
                return;
            }

            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateImportUploadModal($"{InteractionConstants.ImportUploadModal}:{source}",
                    playSource)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportUploadModal)]
    [UsernameSetRequired]
    public async Task ImportUpload(string source)
    {
        if (!Enum.TryParse(source, out PlaySource playSource))
        {
            return;
        }

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequired, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        var importCacheKey = $"import-in-progress-{contextUser.UserId}";
        if (cache.TryGetValue(importCacheKey, out bool _))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You already have an import running. Please wait for it to finish before uploading more files.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
            return;
        }

        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;
        var attachments = this.Context.GetModalFiles("files");

        await RespondAsync(InteractionCallback.DeferredMessage());

        var progress = new StringBuilder();
        var loadingResponse = ImportBuilders.ImportProgress(playSource,
            $"- {EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Loading import files...",
            false);
        var message = await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithComponents(loadingResponse.GetComponentsV2())
            .WithFlags(MessageFlags.IsComponentsV2));

        cache.Set(importCacheKey, true, TimeSpan.FromMinutes(1));

        try
        {
            var plays = playSource == PlaySource.AppleMusicImport
                ? await GetAppleMusicImportPlays(message.Id, contextUser, attachments, progress)
                : await GetSpotifyImportPlays(message.Id, contextUser, attachments, progress);

            if (plays == null)
            {
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var playsWithoutDuplicates =
                await importService.RemoveDuplicateImports(contextUser.UserId, plays);
            await UpdateImportProgress(message.Id, playSource, progress,
                $"- **{playsWithoutDuplicates.Count.Format(numberFormat)}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await importService.InsertImportPlays(contextUser, playsWithoutDuplicates);
                await UpdateImportProgress(message.Id, playSource, progress, "- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    var userHasImportedLastfm = await playService.UserHasImportedLastFm(contextUser.UserId);

                    await userService.SetDataSource(contextUser, userHasImportedLastfm
                        ? DataSource.FullImportThenLastFm
                        : DataSource.MergedDeduplicated);
                    await UpdateImportProgress(message.Id, playSource, progress, "- Updated import setting");
                }
            }

            if (contextUser.DataSource != DataSource.LastFm)
            {
                await indexService.RecalculateTopLists(contextUser);
                await UpdateImportProgress(message.Id, playSource, progress, "- Refreshed top list cache");

                BackgroundJob.Schedule(() => indexService.RecalculateTopLists(contextUser.UserId),
                    TimeSpan.FromMinutes(1));
                BackgroundJob.Schedule(() => indexService.RecalculateTopLists(contextUser.UserId),
                    TimeSpan.FromMinutes(2));
            }

            await importService.UpdateExistingScrobbleSource(contextUser);

            contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            progress.AppendLine("- Import complete!");
            var response = await importBuilders.ImportComplete(new ContextModel(this.Context, contextUser),
                playSource, progress.ToString());
            await UpdateImportMessage(message.Id, response);

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await UpdateImportFailed(message.Id, playSource, progress,
                "- ❌ Sorry, an internal error occurred. Please try again later, or open a help thread on [our server](https://discord.gg/fmbot).");
            await this.Context.HandleCommandException(e, userService, sendReply: false);
        }
        finally
        {
            cache.Remove(importCacheKey);
        }
    }

    private async Task<List<UserPlay>> GetSpotifyImportPlays(ulong messageId, User contextUser,
        IReadOnlyList<Attachment> attachments, StringBuilder progress)
    {
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;
        var imports = await importService.HandleSpotifyFiles(contextUser, attachments);

        if (imports.status == ImportStatus.UnknownFailure)
        {
            await UpdateImportFailed(messageId, PlaySource.SpotifyImport, progress,
                "❌ Invalid Spotify import file. Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.");
            return null;
        }

        if (imports.status == ImportStatus.WrongPackageFailure)
        {
            await UpdateImportFailed(messageId, PlaySource.SpotifyImport, progress,
                "❌ Invalid Spotify import files. You have uploaded the wrong Spotify data package.\n\n" +
                "We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. Instead you have uploaded the 'Account data' package.",
                new MediaGalleryProperties
                {
                    new MediaGalleryItemProperties(
                        new ComponentMediaProperties("https://fm.bot/img/bot/import-spotify-instructions.png"))
                },
                new ActionRowProperties()
                    .AddComponents(new LinkButtonProperties("https://www.spotify.com/us/account/privacy/",
                        "Spotify privacy page")));
            return null;
        }

        if (imports.result == null || imports.result.Count == 0 || imports.result.All(a => a.MsPlayed == 0))
        {
            if (attachments.Any(a => a.FileName != null) &&
                attachments.Any(a => a.FileName.ToLower().Contains("streaminghistory")))
            {
                await UpdateImportFailed(messageId, PlaySource.SpotifyImport, progress,
                    "❌ Invalid Spotify import file. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package.\n\n" +
                    "The files should have names like `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                    "The right files can take some more time to get, but actually contain your full Spotify history. Sorry for the inconvenience.");
                return null;
            }

            await UpdateImportFailed(messageId, PlaySource.SpotifyImport, progress,
                "❌ Invalid Spotify import file (contains no plays). Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                "If your `.zip` contains files like `Userdata.json` or `Identity.json` it's the wrong package. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. ");
            return null;
        }

        await UpdateImportProgress(messageId, PlaySource.SpotifyImport, progress,
            $"- **{imports.result.Count.Format(numberFormat)}** Spotify imports found");

        var plays = await importService.SpotifyImportToUserPlays(contextUser, imports.result);
        await UpdateImportProgress(messageId, PlaySource.SpotifyImport, progress,
            $"- **{plays.Count.Format(numberFormat)}** actual plays found");

        return plays;
    }

    private async Task<List<UserPlay>> GetAppleMusicImportPlays(ulong messageId, User contextUser,
        IReadOnlyList<Attachment> attachments, StringBuilder progress)
    {
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;
        var imports = await importService.HandleAppleMusicFiles(contextUser, attachments[0]);

        if (imports.status == ImportStatus.UnknownFailure)
        {
            await UpdateImportFailed(messageId, PlaySource.AppleMusicImport, progress,
                "❌ Invalid Apple Music import file, or something went wrong.\n\n" +
                "If you've uploaded a `.zip` file you can also try to find the `Apple Music Play Activity.csv` inside the .zip and upload that instead.\n\n" +
                "You can also open a help thread on [our server](https://discord.gg/fmbot).");
            return null;
        }

        if (imports.status == ImportStatus.WrongCsvFailure)
        {
            await UpdateImportFailed(messageId, PlaySource.AppleMusicImport, progress,
                "❌ We couldn't read the `.csv` file that was provided.\n\n" +
                "We can only read an `Apple Music Play Activity.csv` file. Other files do not contain the data required for importing.\n\n" +
                "Still having issues? You can also open a help thread on [our server](https://discord.gg/fmbot).");
            return null;
        }

        await UpdateImportProgress(messageId, PlaySource.AppleMusicImport, progress,
            $"- **{imports.result.Count.Format(numberFormat)}** Apple Music imports found");

        var importsWithArtist = await importService.AppleMusicImportAddArtists(contextUser, imports.result);
        await UpdateImportProgress(messageId, PlaySource.AppleMusicImport, progress,
            $"- **{importsWithArtist.matchFoundPercentage}** of artist names found for imports");
        await UpdateImportProgress(messageId, PlaySource.AppleMusicImport, progress,
            $"- **{importsWithArtist.userPlays.Count(c => !string.IsNullOrWhiteSpace(c.ArtistName)).Format(numberFormat)}** with artist names");

        var plays = ImportService.AppleMusicImportsToValidUserPlays(contextUser, importsWithArtist.userPlays);
        await UpdateImportProgress(messageId, PlaySource.AppleMusicImport, progress,
            $"- **{plays.Count.Format(numberFormat)}** actual plays found");

        return plays;
    }

    private Task UpdateImportProgress(ulong messageId, PlaySource playSource, StringBuilder progress,
        string lineToAdd)
    {
        progress.AppendLine(lineToAdd);
        return UpdateImportMessage(messageId, ImportBuilders.ImportProgress(playSource, progress.ToString()));
    }

    private Task UpdateImportFailed(ulong messageId, PlaySource playSource, StringBuilder progress, string error,
        MediaGalleryProperties image = null, ActionRowProperties components = null)
    {
        progress.AppendLine(error);

        var response = ImportBuilders.ImportProgress(playSource, progress.ToString(), false,
            DiscordConstants.WarningColorOrange);

        if (image != null)
        {
            response.ComponentsContainer.AddComponent(image);
        }

        if (components != null)
        {
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithActionRow(components);
        }

        return UpdateImportMessage(messageId, response);
    }

    private async Task UpdateImportMessage(ulong messageId, ResponseModel response)
    {
        await this.Context.Interaction.ModifyFollowupMessageAsync(messageId, m =>
        {
            m.Components = response.GetComponentsV2();
            m.AllowedMentions = AllowedMentionsProperties.None;
            if (response.Stream != null)
            {
                m.Attachments = [new AttachmentProperties(response.FileName, response.Stream)];
            }
        });

        if (response.Stream != null)
        {
            await response.Stream.DisposeAsync();
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.Modify)]
    public async Task SelectImportModifyPickButton(string pickedOption)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

            if (supporterRequired != null)
            {
                await this.Context.SendResponse(interactivity, supporterRequired, userService);
                await this.Context.LogCommandUsedAsync(supporterRequired, userService);
                return;
            }

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
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
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
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
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
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
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickArtistModal)]
    public async Task PickArtist()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            _ = this.Context.Channel?.TriggerTypingAsync()!;
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await importBuilders.PickArtist(contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator, importRef);

            await this.Context.SendFollowUpResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickAlbumModal)]
    public async Task PickAlbum()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var albumName = this.Context.GetModalValue("album_name");

            _ = this.Context.Channel?.TriggerTypingAsync()!;
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.PickTrackModal)]
    public async Task PickTrack()
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");
            var trackName = this.Context.GetModalValue("track_name");

            _ = this.Context.Channel?.TriggerTypingAsync()!;
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var importRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                importRef);

            await this.Context.SendFollowUpResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistRenameModal)]
    public async Task RenameArtist(string selectedArtistRef)
    {
        try
        {
            var artistName = this.Context.GetModalValue("artist_name");

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newArtistRef =
                importService.StoreImportReference(new ReferencedMusic { Artist = artistName });

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedArtistRef,
                newArtistRef);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newAlbumRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Album = albumName });

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedAlbumRef,
                newAlbumRef);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var newTrackRef = importService.StoreImportReference(new ReferencedMusic
                { Artist = artistName, Track = trackName });

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                selectedTrackRef,
                newTrackRef);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportSetting)]
    [UsernameSetRequired]
    public async Task SetImport()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequired, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValue = stringMenuInteraction.Data.SelectedValues[0];

        if (Enum.TryParse(selectedValue, out DataSource dataSource))
        {
            try
            {
                var name = dataSource.GetAttribute<OptionAttribute>().Name;
                var loadingText =
                    $"Setting import mode to **{name}** and recalculating your stored top artist/albums/tracks...";

                await RespondAsync(InteractionCallback.DeferredModifyMessage);
                await ShowImportModeLoader(loadingText);

                var newUserSettings = await userService.SetDataSource(contextUser, dataSource);
                var recalculateTask = indexService.RecalculateTopLists(newUserSettings);

                var loadingResponse = await userBuilder.ImportMode(new ContextModel(this.Context, contextUser),
                    contextUser.UserId, loadingText: loadingText);
                await this.Context.UpdateInteractionEmbed(loadingResponse, defer: false);

                await recalculateTask;

                var response = await userBuilder.ImportMode(new ContextModel(this.Context, contextUser),
                    contextUser.UserId,
                    "✅ Your stored top artist/albums/tracks have successfully been recalculated.");

                await this.Context.UpdateInteractionEmbed(response, defer: false);
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClearSpotify)]
    [UsernameSetRequired]
    public async Task ClearImportSpotify()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await ShowImportModeLoader("Deleting your imported Spotify history...");

            await importService.RemoveImportedSpotifyPlays(contextUser);
            playService.RemoveAllUserPlaysFromCache(contextUser.UserId);

            var response = await userBuilder.ImportMode(new ContextModel(this.Context, contextUser),
                contextUser.UserId, "✅ All your imported Spotify history has been removed from .fmbot.");

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClearAppleMusic)]
    [UsernameSetRequired]
    public async Task ClearImportAppleMusic()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await ShowImportModeLoader("Deleting your imported Apple Music history...");

            await importService.RemoveImportedAppleMusicPlays(contextUser);
            playService.RemoveAllUserPlaysFromCache(contextUser.UserId);

            var response = await userBuilder.ImportMode(new ContextModel(this.Context, contextUser),
                contextUser.UserId, "✅ All your imported Apple Music history has been removed from .fmbot.");

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task ShowImportModeLoader(string loadingText)
    {
        var message = ((MessageComponentInteraction)this.Context.Interaction).Message;
        var components = message.Components.WithDisabled();

        foreach (var container in components.OfType<ComponentContainerProperties>())
        {
            container.Components = container.Components
                .Where(w => w is not ActionRowProperties)
                .Select(s => s is StringMenuProperties
                    ? new TextDisplayProperties(
                        $"{EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} {loadingText}")
                    : s)
                .ToList();
        }

        await this.Context.Interaction.ModifyResponseAsync(m => m.Components = components);
    }

    [ComponentInteraction(InteractionConstants.ImportManage)]
    [UsernameSetRequired]
    public async Task ImportManage()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequired, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response =
                await userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.Start)]
    [UsernameSetRequired]
    public async Task ModifyImportAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequired, userService);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        if (this.Context.Guild != null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var serverContainer = new ComponentContainerProperties();
            serverContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
            serverContainer.WithTextDisplay("Check your DMs to continue with modifying your .fmbot imports.");

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithComponents([serverContainer])
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        }
        else if (this.Context.Channel != null)
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;
        }

        try
        {
            var response =
                await importBuilders.ImportModify(new ContextModel(this.Context, contextUser),
                    contextUser.UserId);
            var dmChannel = await userService.GetDmChannel(this.Context.User);
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Components = response.GetComponentsV2(),
                Flags = MessageFlags.IsComponentsV2
            });
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsPickSource)]
    [UsernameSetRequired]
    public async Task ImportPickSource()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var supporterRequired =
                ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser),
                    "onboarding-import");

            if (supporterRequired != null)
            {
                await this.Context.SendResponse(interactivity, supporterRequired, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(supporterRequired, userService);
                return;
            }

            var response =
                ImportBuilders.ImportInstructionsPickSource(new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsSpotify)]
    [UsernameSetRequired]
    public async Task ImportInstructionsSpotify()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            this.Context.DeferUpdateInBackground();
            var disableButtonsTask = this.Context.DisableButtonsAndMenus().ObserveFaults();

            var response =
                await importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser));

            await disableButtonsTask;
            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsAppleMusic)]
    [UsernameSetRequired]
    public async Task ImportInstructionsAppleMusic()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            this.Context.DeferUpdateInBackground();
            var disableButtonsTask = this.Context.DisableButtonsAndMenus().ObserveFaults();

            var response =
                await importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser));

            await disableButtonsTask;
            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistRenameConfirmed)]
    public async Task RenameArtistConfirmed(string selectedArtistRef, string newArtistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistDelete)]
    public async Task DeleteArtist(string artistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickArtist(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                artistRef,
                deletion: false);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.ArtistDeleteConfirmed)]
    public async Task DeleteArtistConfirmed(string artistRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumRenameConfirmed)]
    public async Task RenameAlbumConfirmed(string selectedAlbumRef, string newAlbumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumDelete)]
    public async Task DeleteAlbum(string albumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickAlbum(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                albumRef,
                deletion: false);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.AlbumDeleteConfirmed)]
    public async Task DeleteAlbumConfirmed(string albumRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackRenameConfirmed)]
    public async Task RenameTrackConfirmed(string selectedTrackRef, string newTrackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackDelete)]
    public async Task DeleteTrack(string trackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await importBuilders.PickTrack(
                contextUser.UserId,
                contextUser.NumberFormat ?? NumberFormat.NoSeparator,
                trackRef,
                deletion: false);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportModify.TrackDeleteConfirmed)]
    public async Task DeleteTrackConfirmed(string trackRef)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();
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

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
