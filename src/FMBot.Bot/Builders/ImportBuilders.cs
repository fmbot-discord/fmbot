using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class ImportBuilders
{
    private readonly PlayService _playService;
    private readonly ArtistsService _artistsService;
    private readonly AlbumService _albumService;
    private readonly TrackService _trackService;
    private readonly CensorService _censorService;
    private readonly ImportService _importService;
    private readonly GraphService _graphService;

    public ImportBuilders(PlayService playService, ArtistsService artistsService, CensorService censorService,
        AlbumService albumService, TrackService trackService, ImportService importService, GraphService graphService)
    {
        this._playService = playService;
        this._artistsService = artistsService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._trackService = trackService;
        this._importService = importService;
        this._graphService = graphService;
    }

    public static ResponseModel ImportSupporterRequired(ContextModel context, string source = "importing")
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        if (context.ContextUser.UserType == UserType.User)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                "Only supporters can import and access their Spotify or Apple Music history in .fmbot."));

            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.AddComponent(new ActionRowProperties()
                .AddComponents(new ButtonProperties(
                    InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: source),
                    Constants.GetSupporterButton, ButtonStyle.Primary))
                .AddComponents(new LinkButtonProperties("https://fm.bot/importing/", "Import info")));

            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public static ResponseModel ImportInstructionsPickSource(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        response.ComponentsContainer.AddComponent(
            new TextDisplayProperties("What music service history would you like to import?"));

        response.ComponentsContainer.WithSeparator();
        response.ComponentsContainer.AddComponent(new ActionRowProperties()
            .WithButton("Spotify", InteractionConstants.ImportInstructionsSpotify,
                emote: EmojiProperties.Custom(DiscordConstants.Spotify))
            .WithButton("Apple Music", InteractionConstants.ImportInstructionsAppleMusic,
                emote: EmojiProperties.Custom(DiscordConstants.AppleMusic)));

        return response;
    }

    public async Task<ResponseModel> GetSpotifyImportInstructions(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.SpotifyColorGreen);

        response.ComponentsContainer.AddComponent(new TextDisplayProperties("## Spotify import instructions"));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var requesting = new StringBuilder();
        requesting.AppendLine($"### {EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} Requesting your data from Spotify");
        requesting.AppendLine(
            "1. Go to your **[Spotify privacy settings](https://www.spotify.com/us/account/privacy/)**");
        requesting.AppendLine("2. Scroll down to \"Download your data\"");
        requesting.AppendLine("3. Select **Extended streaming history**");
        requesting.AppendLine("4. De-select the other options");
        requesting.AppendLine("5. Press request data");
        requesting.AppendLine("6. Confirm your data request through your email");
        requesting.AppendLine("7. Wait up to 30 days for Spotify to deliver your files (usually takes 7)");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(requesting.ToString()));
        response.ComponentsContainer.AddComponents(
            new ActionRowProperties().AddComponents(new LinkButtonProperties(
                $"https://www.spotify.com/us/account/privacy/", "Spotify privacy page")));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var importing = new StringBuilder();
        importing.AppendLine($"### {EmojiProperties.Custom(DiscordConstants.Imports).ToDiscordString("imports")} Importing your data into .fmbot");
        importing.AppendLine("1. Download the file Spotify provided");
        importing.AppendLine("2. Press **Upload files** below and add the `.zip` file");
        importing.AppendLine("3. Having issues? You can also upload the `.json` files separately, up to 10 at a time");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(importing.ToString()));
        response.ComponentsContainer.AddComponents(new ActionRowProperties()
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.ImportUpload}:{nameof(PlaySource.SpotifyImport)}", "Upload files",
                ButtonStyle.Primary)));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var notes = new StringBuilder();
        notes.AppendLine("### 📝 Notes");
        notes.AppendLine(
            "- We filter out duplicates and skips, so don't worry about submitting the same file twice");
        notes.AppendLine("- The importing service is only available with an active supporter subscription");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(notes.ToString()));

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.SpotifyImport);
        if (count > 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

            var importedPlays = new StringBuilder();
            importedPlays.AppendLine("### ⚙️ Your imported Spotify plays");
            importedPlays.AppendLine(
                $"You have already imported **{count.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the buttons below.");
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(importedPlays.ToString()));
            response.ComponentsContainer.AddComponent(new ActionRowProperties()
                .AddComponents(new ButtonProperties(
                    InteractionConstants.ImportManage, "Manage import settings", ButtonStyle.Secondary))
                .AddComponents(new ButtonProperties(
                    InteractionConstants.ImportModify.Start, "Modify imports", ButtonStyle.Secondary)));
        }

        return response;
    }

    public async Task<ResponseModel> GetAppleMusicImportInstructions(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.AppleMusicRed);

        response.ComponentsContainer.AddComponent(new TextDisplayProperties("## Apple Music import instructions"));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var requesting = new StringBuilder();
        requesting.AppendLine($"### {EmojiProperties.Custom(DiscordConstants.AppleMusic).ToDiscordString("apple_music")} Requesting your data from Apple");
        requesting.AppendLine("1. Go to your **[Apple privacy settings](https://privacy.apple.com/)**");
        requesting.AppendLine("2. Click on **Request a copy of your data**");
        requesting.AppendLine("3. Select **Apple Media Services Information**");
        requesting.AppendLine("4. De-select the other options");
        requesting.AppendLine("5. Press **Continue**");
        requesting.AppendLine("6. Press **Complete request**");
        requesting.AppendLine("7. Wait up to 7 days for Apple to deliver your files");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(requesting.ToString()));
        response.ComponentsContainer.AddComponents(
            new ActionRowProperties().AddComponents(new LinkButtonProperties(
                $"https://privacy.apple.com/", "Apple Data and Privacy")));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var importing = new StringBuilder();
        importing.AppendLine($"### {EmojiProperties.Custom(DiscordConstants.Imports).ToDiscordString("imports")} Importing your data into .fmbot");
        importing.AppendLine("1. Download the file Apple provided");
        importing.AppendLine("2. Press **Upload file** below and add the `.zip` file");
        importing.AppendLine(
            "3. Got multiple zip files? You can try them all until one succeeds. Only one of them contains your play history");
        importing.AppendLine(
            "4. Having issues? You can also upload the `Apple Music Play Activity.csv` file separately");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(importing.ToString()));
        response.ComponentsContainer.AddComponents(new ActionRowProperties()
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.ImportUpload}:{nameof(PlaySource.AppleMusicImport)}", "Upload file",
                ButtonStyle.Primary)));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var notes = new StringBuilder();
        notes.AppendLine("### 📝 Notes");
        notes.AppendLine(
            "- Apple provides their history data without artist names. We try to find these as best as possible based on the album and track name.");
        notes.AppendLine(
            "- Exceeding Discord file limits? Try on [our server](https://discord.gg/fmbot) in #commands.");
        notes.AppendLine("- The importing service is only available with an active supporter subscription");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(notes.ToString()));

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.AppleMusicImport);
        if (count > 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

            var importedPlays = new StringBuilder();
            importedPlays.AppendLine("### ⚙️ Your imported Apple Music plays");
            importedPlays.AppendLine(
                $"You have already imported **{count.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the buttons below.");
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(importedPlays.ToString()));
            response.ComponentsContainer.AddComponent(new ActionRowProperties()
                .AddComponents(new ButtonProperties(
                    InteractionConstants.ImportManage, "Manage import settings", ButtonStyle.Secondary))
                .AddComponents(new ButtonProperties(
                    InteractionConstants.ImportModify.Start, "Modify imports", ButtonStyle.Secondary)));
        }

        return response;
    }

    public static ResponseModel ImportProgress(PlaySource playSource, string progress, bool loading = true,
        Color? accentColor = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var emoji = playSource == PlaySource.AppleMusicImport
            ? EmojiProperties.Custom(DiscordConstants.AppleMusic).ToDiscordString("apple_music")
            : EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify");

        response.ComponentsContainer.WithAccentColor(accentColor ?? DiscordConstants.InformationColorBlue);
        response.ComponentsContainer.WithTextDisplay($"### {emoji} Importing history into .fmbot..");
        response.ComponentsContainer.WithTextDisplay(loading
            ? progress + $"- {EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Processing..."
            : progress);

        return response;
    }

    public async Task<ResponseModel> ImportComplete(ContextModel context, PlaySource playSource, string progress)
    {
        var appleMusic = playSource == PlaySource.AppleMusicImport;
        var serviceName = appleMusic ? "Apple Music" : "Spotify";

        var response = ImportProgress(playSource, progress, false,
            appleMusic ? DiscordConstants.AppleMusicRed : DiscordConstants.SpotifyColorGreen);
        var container = response.ComponentsContainer;

        var importActivated = new StringBuilder();
        var importSetting = new StringBuilder();

        switch (context.ContextUser.DataSource)
        {
            case DataSource.LastFm:
                importActivated.AppendLine(
                    "Your import setting is currently still set to just Last.fm, so imports will not be used. You can change this manually with the button below.");
                break;
            case DataSource.FullImportThenLastFm:
                importActivated.AppendLine(
                    "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                importSetting.AppendLine(
                    $"Your import setting has been set to **Full imports, then Last.fm**. This uses your full {serviceName} history and adds your Last.fm scrobbles afterwards.");
                break;
            case DataSource.ImportThenFullLastFm:
                importActivated.AppendLine(
                    "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                importSetting.AppendLine(
                    $"Your import setting has been set to **Imports, then full Last.fm**. This uses your {serviceName} history up until you started using Last.fm.");
                break;
            case DataSource.MergedDeduplicated:
                importActivated.AppendLine(
                    "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                importSetting.AppendLine(
                    $"Your import setting has been set to **Smart deduplication**. This combines your full {serviceName} history with your Last.fm scrobbles and removes imported plays you already scrobbled to Last.fm.");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        container.WithSeparator();
        container.WithTextDisplay($"**✅ Importing service activated**\n{importActivated}");

        if (importSetting.Length > 0)
        {
            container.WithTextDisplay($"**⚙️ Current import setting**\n{importSetting}");
        }

        var plays = await this._playService.GetAllUserPlays(context.ContextUser.UserId);
        var importGraph = this._graphService.BuildImportGraph(context, response, plays,
            context.ContextUser.DataSource, "imports.png");
        if (importGraph != null)
        {
            container.WithSeparator();
            container.AddComponent(importGraph);
        }

        container.WithSeparator();
        container.WithActionRow(new ActionRowProperties()
            .AddComponents(new ButtonProperties($"{InteractionConstants.RecapAlltime}:{context.ContextUser.UserId}",
                "View your stats", ButtonStyle.Primary))
            .AddComponents(new ButtonProperties(InteractionConstants.ImportManage, "Manage import settings",
                ButtonStyle.Secondary)));

        return response;
    }

    public async Task<ResponseModel> ImportModify(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var allPlays = await this._playService.GetAllUserPlays(userId, false);
        var hasImported = allPlays.Any(a =>
            a.PlaySource == PlaySource.SpotifyImport || a.PlaySource == PlaySource.AppleMusicImport);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        var description = new StringBuilder();
        description.AppendLine("### ✏️ Select what you want to modify");
        description.AppendLine(
            "Please keep in mind that this only modifies imports that are stored in .fmbot. It doesn't modify any of your Last.fm scrobbles or data.");

        if (!hasImported)
        {
            description.AppendLine();
            description.AppendLine(
                "Run the `.import` command to see how to request your data and to get started with imports. " +
                "After importing you'll be able to use this command.");
        }

        container.WithTextDisplay(description.ToString());

        if (hasImported)
        {
            container.WithSeparator();

            var storedDescription = new StringBuilder();
            if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport).Format(context.NumberFormat)} imported Apple Music plays");
            }

            if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport).Format(context.NumberFormat)} imported Spotify plays");
            }

            container.WithTextDisplay(
                $"**{EmojiProperties.Custom(DiscordConstants.Imports).ToDiscordString("imports")} Your stored imports**\n{storedDescription}");

            var noteDescription = new StringBuilder();
            if (context.ContextUser.DataSource == DataSource.ImportThenFullLastFm)
            {
                noteDescription.AppendLine(
                    "Because you have selected the mode **Imports, then full Last.fm** not all imports might be used. This mode only uses your imports up until you started scrobbling on Last.fm.");
            }

            if (context.ContextUser.DataSource == DataSource.MergedDeduplicated)
            {
                noteDescription.AppendLine(
                    "Because you have selected the mode **Smart deduplication**, imported plays that you also scrobbled to Last.fm are removed, keeping your Last.fm scrobble. All other imports are used.");
            }

            if (noteDescription.Length > 0)
            {
                container.WithTextDisplay($"**📝 How your imports are used**\n{noteDescription}");
            }

            container.WithTextDisplay(
                "**🗑️ Deleting imports**\nTo delete all of your imports, use 'Manage import settings' and set your source to Last.fm.");
        }

        container.WithSeparator();
        container.WithActionRow(new ActionRowProperties()
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.ImportModify.Modify}:{nameof(ImportModifyPick.Artist)}", "Artist",
                ButtonStyle.Secondary) { Disabled = !hasImported })
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.ImportModify.Modify}:{nameof(ImportModifyPick.Album)}", "Album",
                ButtonStyle.Secondary) { Disabled = !hasImported })
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.ImportModify.Modify}:{nameof(ImportModifyPick.Track)}", "Track",
                ButtonStyle.Secondary) { Disabled = !hasImported })
            .AddComponents(new ButtonProperties(InteractionConstants.ImportManage, "Manage import settings",
                ButtonStyle.Secondary) { Disabled = !hasImported }));

        return response;
    }

    public async Task<ResponseModel> PickArtist(int userId, NumberFormat numberFormat, string importRef,
        string newImportRef = null, string oldImportRef = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        var artistName = this._importService.GetImportRef(importRef)?.Artist;

        if (artistName == null)
        {
            container.WithAccentColor(DiscordConstants.WarningColorOrange);
            container.WithTextDisplay("### Modifying your imports\nImport modify expired. Please start again.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var artist = await this._artistsService.GetArtistFromDatabase(artistName, false);
        var capitalizedArtistName = artist?.Name ?? artistName;
        container.WithTextDisplay($"### Modifying your imports\n- Artist: **{capitalizedArtistName}**");
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);
        allPlays = allPlays
            .Where(w => w.ArtistName != null && w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var processedPlays = await this._playService
            .GetAllUserPlays(userId);
        processedPlays = processedPlays
            .Where(w => w.ArtistName != null && w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AddImportPickCounts(container, numberFormat, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == true)
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay("**Imports deleted**\nYour imports for this artist have been deleted.");
            }
            else
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Warning ⚠️**\nThis will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm).Format(numberFormat)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.ArtistDeleteConfirmed}:{importRef}",
                        "Confirm deletion", ButtonStyle.Danger)));
            }
        }
        else
        {
            var oldArtistName = this._importService.GetImportRef(oldImportRef)?.Artist;
            var newArtistName = this._importService.GetImportRef(newImportRef)?.Artist;

            if (string.IsNullOrWhiteSpace(newArtistName))
            {
                container.WithAccentColor(DiscordConstants.InformationColorBlue);
                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.ArtistRename}:{importRef}",
                        "Edit artist imports", ButtonStyle.Secondary))
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.ArtistDelete}:{importRef}",
                        "Delete imports", ButtonStyle.Danger)));
            }
            else if (oldArtistName == null)
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Confirm your edit ⚠️**\n`{capitalizedArtistName}` to `{newArtistName}`");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.ArtistRenameConfirmed}:{importRef}:{newImportRef}",
                        "Confirm edit", ButtonStyle.Secondary)));
            }
            else
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay(
                    $"**Imports successfully edited ✅**\n`{oldArtistName}` to `{newArtistName}`");
                container.WithTextDisplay(
                    "**Note about future imports**\nUsually when you import, duplicates will be filtered out. However, note that since your imports are now edited, there might be duplicates when you import the same service again.");
            }
        }

        return response;
    }

    public async Task<ResponseModel> PickAlbum(int userId, NumberFormat numberFormat, string importRef,
        string newImportRef = null, string oldImportRef = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        var albumRef = this._importService.GetImportRef(importRef);

        if (albumRef?.Artist == null || albumRef?.Album == null)
        {
            container.WithAccentColor(DiscordConstants.WarningColorOrange);
            container.WithTextDisplay("### Modifying your imports\nImport modify expired. Please start again.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var artistName = albumRef.Artist;
        var albumName = albumRef.Album;

        var album = await this._albumService.GetAlbumFromDatabase(artistName, albumName, false);
        var capitalizedArtistName = album?.ArtistName ?? artistName;
        var capitalizedAlbumName = album?.Name ?? albumName;

        container.WithTextDisplay("### Modifying your imports\n" +
                                  $"- Artist: **{capitalizedArtistName}**\n" +
                                  $"- Album: **{capitalizedAlbumName}**");
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);
        allPlays = allPlays
            .Where(w => w.ArtistName != null &&
                        w.AlbumName != null &&
                        w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                        w.AlbumName.Equals(albumName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var processedPlays = await this._playService
            .GetAllUserPlays(userId);
        processedPlays = processedPlays
            .Where(w => w.ArtistName != null &&
                        w.AlbumName != null &&
                        w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                        w.AlbumName.Equals(albumName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AddImportPickCounts(container, numberFormat, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == false)
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Warning ⚠️**\nThis will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm).Format(numberFormat)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.AlbumDeleteConfirmed}:{importRef}",
                        "Confirm deletion", ButtonStyle.Danger)));
            }
            else
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay(
                    $"**Imports successfully deleted ✅**\nRemoved `{capitalizedAlbumName}` by `{capitalizedArtistName}`");
            }
        }
        else
        {
            var oldAlbumRef = this._importService.GetImportRef(oldImportRef);
            var newAlbumRef = this._importService.GetImportRef(newImportRef);

            if (string.IsNullOrWhiteSpace(newImportRef))
            {
                container.WithAccentColor(DiscordConstants.InformationColorBlue);
                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.AlbumRename}:{importRef}",
                        "Edit album imports", ButtonStyle.Secondary))
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.AlbumDelete}:{importRef}",
                        "Delete imports", ButtonStyle.Danger)));
            }
            else if (oldAlbumRef == null)
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Confirm your edit ⚠️**\n`{capitalizedAlbumName}` by `{capitalizedArtistName}` to `{newAlbumRef.Album}` by `{newAlbumRef.Artist}`");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.AlbumRenameConfirmed}:{importRef}:{newImportRef}",
                        "Confirm edit", ButtonStyle.Secondary)));
            }
            else
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay(
                    $"**Imports successfully edited ✅**\n`{oldAlbumRef.Album}` by `{oldAlbumRef.Artist}` to `{newAlbumRef.Album}` by `{newAlbumRef.Artist}`");
                container.WithTextDisplay(
                    "**Note about future imports**\nUsually when you import, duplicates will be filtered out. However, note that since your imports are now edited, there might be duplicates when you import the same service again.");
            }
        }

        return response;
    }

    public async Task<ResponseModel> PickTrack(int userId, NumberFormat numberFormat, string importRef,
        string newImportRef = null, string oldImportRef = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        var trackRef = this._importService.GetImportRef(importRef);

        if (trackRef?.Artist == null || trackRef?.Track == null)
        {
            container.WithAccentColor(DiscordConstants.WarningColorOrange);
            container.WithTextDisplay("### Modifying your imports\nImport modify expired. Please start again.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var artistName = trackRef.Artist;
        var trackName = trackRef.Track;

        var track = await this._trackService.GetTrackFromDatabase(artistName, trackName);
        var capitalizedArtistName = track?.ArtistName ?? artistName;
        var capitalizedTrackName = track?.Name ?? trackName;

        container.WithTextDisplay("### Modifying your imports\n" +
                                  $"- Artist: **{capitalizedArtistName}**\n" +
                                  $"- Track: **{capitalizedTrackName}**");
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);
        allPlays = allPlays
            .Where(w => w.ArtistName != null &&
                        w.TrackName != null &&
                        w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                        w.TrackName.Equals(trackName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var processedPlays = await this._playService
            .GetAllUserPlays(userId);
        processedPlays = processedPlays
            .Where(w => w.ArtistName != null &&
                        w.TrackName != null &&
                        w.ArtistName.Equals(artistName, StringComparison.OrdinalIgnoreCase) &&
                        w.TrackName.Equals(trackName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AddImportPickCounts(container, numberFormat, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == false)
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Warning ⚠️**\nThis will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm).Format(numberFormat)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.TrackDeleteConfirmed}:{importRef}",
                        "Confirm deletion", ButtonStyle.Danger)));
            }
            else
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay(
                    $"**Imports successfully deleted ✅**\nRemoved `{capitalizedTrackName}` by `{capitalizedArtistName}`");
            }
        }
        else
        {
            var oldTrackRef = this._importService.GetImportRef(oldImportRef);
            var newTrackRef = this._importService.GetImportRef(newImportRef);

            if (string.IsNullOrWhiteSpace(newImportRef))
            {
                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.TrackRename}:{importRef}",
                        "Edit track imports", ButtonStyle.Secondary))
                    .AddComponents(new ButtonProperties($"{InteractionConstants.ImportModify.TrackDelete}:{importRef}",
                        "Delete imports", ButtonStyle.Danger)));
            }
            else if (oldTrackRef == null)
            {
                container.WithAccentColor(DiscordConstants.WarningColorOrange);
                container.WithTextDisplay(
                    $"**Confirm your edit ⚠️**\n`{capitalizedTrackName}` by `{capitalizedArtistName}` to `{newTrackRef.Track}` by `{newTrackRef.Artist}`");

                container.WithSeparator();
                container.WithActionRow(new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.ImportModify.TrackRenameConfirmed}:{importRef}:{newImportRef}",
                        "Confirm edit", ButtonStyle.Secondary)));
            }
            else
            {
                container.WithAccentColor(DiscordConstants.SuccessColorGreen);
                container.WithTextDisplay(
                    $"**Imports successfully edited ✅**\n`{oldTrackRef.Track}` by `{oldTrackRef.Artist}` to `{newTrackRef.Track}` by `{newTrackRef.Artist}`");
                container.WithTextDisplay(
                    "**Note about future imports**\nUsually when you import, duplicates will be filtered out. However, note that since your imports are now edited, there might be duplicates when you import the same service again.");
            }
        }

        return response;
    }

    private static void AddImportPickCounts(ComponentContainerProperties container, NumberFormat numberFormat,
        ICollection<UserPlay> allPlays,
        ICollection<UserPlay> processedPlays)
    {
        var totalDescription = new StringBuilder();
        totalDescription.AppendLine($"- {allPlays.Count.Format(numberFormat)} total plays");
        if (allPlays.Any(c => c.PlaySource == PlaySource.LastFm))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm).Format(numberFormat)} Last.fm scrobbles");
        }

        if (allPlays.Any(c => c.PlaySource == PlaySource.SpotifyImport))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport).Format(numberFormat)} Spotify imports");
        }

        if (allPlays.Any(c => c.PlaySource == PlaySource.AppleMusicImport))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport).Format(numberFormat)} Apple Music imports");
        }

        container.WithTextDisplay($"**Total playcounts - Including overlapping/duplicate plays**\n{totalDescription}");

        var processedDescription = new StringBuilder();
        processedDescription.AppendLine($"- {processedPlays.Count().Format(numberFormat)} total plays");
        if (processedPlays.Any(c => c.PlaySource == PlaySource.LastFm))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.LastFm).Format(numberFormat)} of those are Last.fm scrobbles");
        }

        if (processedPlays.Any(c => c.PlaySource == PlaySource.SpotifyImport))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport).Format(numberFormat)} of those are Spotify imports");
        }

        if (processedPlays.Any(c => c.PlaySource == PlaySource.AppleMusicImport))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport).Format(numberFormat)} of those are Apple Music imports");
        }

        container.WithTextDisplay($"**Final playcounts - Overlapping plays filtered**\n{processedDescription}");
    }
}
