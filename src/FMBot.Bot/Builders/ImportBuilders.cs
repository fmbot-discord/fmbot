using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class ImportBuilders
{
    private readonly PlayService _playService;
    private readonly ArtistsService _artistsService;
    private readonly AlbumService _albumService;
    private readonly TrackService _trackService;
    private readonly CensorService _censorService;

    public ImportBuilders(PlayService playService, ArtistsService artistsService, CensorService censorService,
        AlbumService albumService, TrackService trackService)
    {
        this._playService = playService;
        this._artistsService = artistsService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._trackService = trackService;
    }

    public static ResponseModel ImportSupporterRequired(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.UserType == UserType.User)
        {
            response.Embed.WithDescription($"Only supporters can import and use their Spotify or Apple Music history.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault)
                .WithButton("Import info", style: ButtonStyle.Link, url: "https://fmbot.xyz/importing/");
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public ResponseModel ImportInstructionsPickSource(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);


        var description = new StringBuilder();

        description.AppendLine("What music service history would you like to import?");

        response.Components = new ComponentBuilder()
            .WithButton("Spotify", InteractionConstants.ImportInstructionsSpotify,
                emote: Emote.Parse("<:spotify:882221219334725662>"))
            .WithButton("Apple Music", InteractionConstants.ImportInstructionsAppleMusic,
                emote: Emote.Parse("<:apple_music:1218182727149420544>"));

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public async Task<ResponseModel> GetSpotifyImportInstructions(ContextModel context,
        bool warnAgainstPublicFiles = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

        response.Embed.WithTitle("Spotify import instructions");

        var requestDescription = new StringBuilder();

        requestDescription.AppendLine(
            "1. Go to your **[Spotify privacy settings](https://www.spotify.com/us/account/privacy/)**");
        requestDescription.AppendLine("2. Scroll down to \"Download your data\"");
        requestDescription.AppendLine("3. Select **Extended streaming history**");
        requestDescription.AppendLine("4. De-select the other options");
        requestDescription.AppendLine("5. Press request data");
        requestDescription.AppendLine("6. Confirm your data request through your email");
        requestDescription.AppendLine("7. Wait up to 30 days for Spotify to deliver your files");
        response.Embed.AddField($"{DiscordConstants.Spotify} Requesting your data from Spotify",
            requestDescription.ToString());

        var importDescription = new StringBuilder();

        importDescription.AppendLine("1. Download the file Spotify provided");
        importDescription.AppendLine(
            $"2. Use the `/import spotify` slash command and add the `.zip` file as an attachment through the options");
        importDescription.AppendLine("3. Having issues? You can also attach each `.json` file separately");
        response.Embed.AddField($"{DiscordConstants.Imports} Importing your data into .fmbot",
            importDescription.ToString());

        var notesDescription = new StringBuilder();
        notesDescription.AppendLine(
            "- We filter out duplicates and skips, so don't worry about submitting the same file twice");
        notesDescription.AppendLine("- The importing service is only available with an active supporter subscription");
        response.Embed.AddField("📝 Notes", notesDescription.ToString());

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.SpotifyImport);
        if (count > 0)
        {
            response.Embed.AddField($"⚙️ Your imported Spotify plays",
                $"You have already imported **{count}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the button below.");
        }

        var footer = new StringBuilder();
        if (warnAgainstPublicFiles)
        {
            footer.AppendLine("Do not share your import files publicly");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());
        response.Components = new ComponentBuilder()
            .WithButton("Spotify privacy page", style: ButtonStyle.Link,
                url: "https://www.spotify.com/us/account/privacy/");

        if (count > 0)
        {
            response.Components.WithButton("Manage import settings", InteractionConstants.ImportManage,
                style: ButtonStyle.Secondary);
        }

        return response;
    }

    public async Task<ResponseModel> GetAppleMusicImportInstructions(ContextModel context,
        bool warnAgainstPublicFiles = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        response.Embed.WithTitle("Apple Music import instructions");

        var requestDescription = new StringBuilder();
        requestDescription.AppendLine("1. Go to your **[Apple privacy settings](https://privacy.apple.com/)**");
        requestDescription.AppendLine("2. Sign in to your account");
        requestDescription.AppendLine("3. Click on **Request a copy of your data**");
        requestDescription.AppendLine("4. Select **Apple Media Services Information**");
        requestDescription.AppendLine("5. De-select the other options");
        requestDescription.AppendLine("6. Press **Continue**");
        requestDescription.AppendLine("7. Press **Complete request**");
        requestDescription.AppendLine("8. Wait up to 7 days for Apple to deliver your files");
        response.Embed.AddField($"{DiscordConstants.AppleMusic} Requesting your data from Apple",
            requestDescription.ToString());

        var importDescription = new StringBuilder();
        importDescription.AppendLine("1. Download the file Apple provided");
        importDescription.AppendLine(
            "2. Use the `/import applemusic` slash command and add the `.zip` file as an attachment through the options");
        importDescription.AppendLine(
            "3. Got multiple zip files? You can try them all until one succeeds. Only one of them contains your play history");
        importDescription.AppendLine(
            "4. Having issues? You can also attach the `Apple Music Play Activity.csv` file separately");

        response.Embed.AddField($"{DiscordConstants.Imports} Importing your data into .fmbot",
            importDescription.ToString());

        var notes = new StringBuilder();
        notes.AppendLine(
            "- Apple provides their history data without artist names. We try to find these as best as possible based on the album and track name.");
        notes.AppendLine(
            "- Exceeding Discord file limits? Try on [our server](https://discord.gg/fmbot) in #commands.");
        notes.AppendLine("- The importing service is only available with an active supporter subscription");
        response.Embed.AddField("📝 Notes", notes.ToString());

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.AppleMusicImport);
        if (count > 0)
        {
            response.Embed.AddField($"⚙️ Your imported Apple Music plays",
                $"You have already imported **{count}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the button below.");
        }

        var footer = new StringBuilder();
        if (warnAgainstPublicFiles)
        {
            footer.AppendLine("Do not share your import files publicly");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());

        response.Components = new ComponentBuilder()
            .WithButton("Apple Data and Privacy", style: ButtonStyle.Link, url: "https://privacy.apple.com/");

        if (count > 0)
        {
            response.Components.WithButton("Manage import settings", InteractionConstants.ImportManage,
                style: ButtonStyle.Secondary);
        }

        return response;
    }

    public async Task<string> GetImportedYears(int userId, PlaySource playSource, NumberFormat numberFormat)
    {
        var years = new StringBuilder();
        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);

        var yearGroups = allPlays
            .Where(w => w.PlaySource == playSource)
            .OrderBy(o => o.TimePlayed)
            .GroupBy(g => g.TimePlayed.Year);

        foreach (var year in yearGroups)
        {
            var playcount = year.Count();
            years.AppendLine(
                $"**`{year.Key}`** " +
                $"- **{playcount.Format(numberFormat)}** {StringExtensions.GetPlaysString(playcount)}");
        }

        return years.Length > 0 ? years.ToString() : null;
    }

    public async Task<ResponseModel> ImportModify(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var allPlays = await this._playService.GetAllUserPlays(userId, false);
        var hasImported = allPlays.Any(a =>
            a.PlaySource == PlaySource.SpotifyImport || a.PlaySource == PlaySource.AppleMusicImport);

        var disabled = !hasImported && context.ContextUser.DataSource == DataSource.LastFm;

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.Components = new ComponentBuilder()
            .WithButton("Artist",
                $"{InteractionConstants.ImportModify.Modify}-{ImportModifyPick.Artist.ToString()}", disabled: disabled)
            .WithButton("Album",
                $"{InteractionConstants.ImportModify.Modify}-{ImportModifyPick.Album.ToString()}", disabled: disabled)
            .WithButton("Track",
                $"{InteractionConstants.ImportModify.Modify}-{ImportModifyPick.Track.ToString()}", disabled: disabled)
            .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary,
                disabled: disabled);

        var embedDescription = new StringBuilder();
        embedDescription.AppendLine(
            "Please keep in mind that this only modifies imports that are stored in .fmbot. It doesn't modify any of your Last.fm scrobbles or data.");
        embedDescription.AppendLine();

        if (!hasImported)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                "Run the `.import` command to see how to request your data and to get started with imports. " +
                "After importing you'll be able to use this command.");
        }

        response.Embed.AddField("✏️ Select what you want to modify",
            embedDescription.ToString());

        if (!hasImported)
        {
            return response;
        }

        {
            var storedDescription = new StringBuilder();
            if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport)} imported Apple Music plays");
            }

            if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport)} imported Spotify plays");
            }

            response.Embed.AddField($"{DiscordConstants.Imports} Your stored imports", storedDescription.ToString());

            var noteDescription = new StringBuilder();
            if (context.ContextUser.DataSource == DataSource.ImportThenFullLastFm)
            {
                noteDescription.AppendLine(
                    "Because you have selected the mode **Imports, then full Last.fm** not all imports might be used. This mode only uses your imports up until you started scrobbling on Last.fm.");
            }

            if (noteDescription.Length > 0)
            {
                response.Embed.AddField($"📝 How your imports are used", noteDescription.ToString());
            }

            response.Embed.AddField($"🗑️ Deleting imports", "To delete all of your imports, use  'Manage import settings' and set your source to Last.fm.");
        }

        return response;
    }

    public async Task<ResponseModel> PickArtist(int userId, string artistName, string newArtistName = null,
        string oldArtistName = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artist = await this._artistsService.GetArtistFromDatabase(artistName, false);
        var capitalizedArtistName = artist?.Name ?? artistName;
        response.Embed.AddField("Modifying your imports", $"- Artist: **{capitalizedArtistName}**");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

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

        AddImportPickCounts(response.Embed, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == true)
            {
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                response.Embed.AddField("Imports deleted",
                    $"Your imports for this artist have been deleted.");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Warning ⚠️",
                    $"This will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm deletion", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.ArtistDeleteConfirmed}——{capitalizedArtistName}");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(newArtistName))
            {
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                response.Components = new ComponentBuilder()
                    .WithButton("Edit artist imports", style: ButtonStyle.Secondary,
                        customId: $"{InteractionConstants.ImportModify.ArtistRename}——{capitalizedArtistName}")
                    .WithButton("Delete imports", style: ButtonStyle.Danger,
                        customId: $"{InteractionConstants.ImportModify.ArtistDelete}——{capitalizedArtistName}");
            }
            else if (oldArtistName == null)
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Confirm your edit ⚠️",
                    $"`{capitalizedArtistName}` to `{newArtistName}`");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm edit", style: ButtonStyle.Secondary,
                        customId:
                        $"{InteractionConstants.ImportModify.ArtistRenameConfirmed}——{capitalizedArtistName}——{newArtistName}");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                response.Embed.AddField("Imports successfully edited ✅",
                    $"`{oldArtistName}` to `{newArtistName}`");
                response.Embed.AddField("Note about future imports",
                    $"Usually when you imports duplicates will be filtered out. However, note that now that your imports are edited there might be duplicates when you import the same service again.");
                response.Components = null;
            }
        }

        return response;
    }

    public async Task<ResponseModel> PickAlbum(int userId, string artistName, string albumName, string newArtistName = null,
        string newAlbumName = null, string oldArtistName = null, string oldAlbumName = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var album = await this._albumService.GetAlbumFromDatabase(artistName, albumName, false);
        var capitalizedArtistName = album?.ArtistName ?? artistName;
        var capitalizedAlbumName = album?.Name ?? albumName;

        response.Embed.AddField("Modifying your imports",
            $"- Artist: **{capitalizedArtistName}**\n" +
            $"- Album: **{capitalizedAlbumName}**");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

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

        AddImportPickCounts(response.Embed, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == false)
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Warning ⚠️",
                    $"This will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm deletion", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.AlbumDeleteConfirmed}-{capitalizedArtistName}——{capitalizedAlbumName}");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
                response.Embed.AddField("Imports successfully deleted ✅",
                    $"Removed `{capitalizedAlbumName}` by `{capitalizedArtistName}`");
                response.Components = null;
            }
        }
        else
        {
            if (newArtistName == null && newAlbumName == null)
            {
                response.Components = new ComponentBuilder()
                    .WithButton("Edit album imports", style: ButtonStyle.Secondary,
                        customId:
                        $"{InteractionConstants.ImportModify.AlbumRename}-{capitalizedArtistName}——{capitalizedAlbumName}")
                    .WithButton("Delete imports", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.AlbumDelete}-{capitalizedArtistName}——{capitalizedAlbumName}");
            }
            else if (oldArtistName == null && oldAlbumName == null)
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Warning ⚠️",
                    $"This will rename **{processedPlays.Count}** imported plays. \n" +
                    "This action can't be undone.");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm rename", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.AlbumRenameConfirmed}-{capitalizedArtistName}——{capitalizedAlbumName}——{newArtistName}——{newAlbumName}");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
                response.Embed.AddField("Imports successfully edited ✅",
                    $"`{oldAlbumName}` by `{oldArtistName}` to `{newAlbumName}` by `{newArtistName}`");
                response.Embed.AddField("Note about future imports",
                    $"Usually when you imports duplicates will be filtered out. However, note that now that your imports are edited there might be duplicates when you import the same service again.");
                response.Components = null;
            }
        }

        return response;
    }

    public async Task<ResponseModel> PickTrack(int userId, string artistName, string trackName, string newArtistName = null,
        string newTrackName = null, string oldArtistName = null, string oldTrackName = null, bool? deletion = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var track = await this._trackService.GetTrackFromDatabase(artistName, trackName);
        var capitalizedArtistName = track?.ArtistName ?? artistName;
        var capitalizedTrackName = track?.Name ?? trackName;

        response.Embed.AddField("Modifying your imports",
            $"- Artist: **{capitalizedArtistName}**\n" +
            $"- Track: **{capitalizedTrackName}**");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

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

        AddImportPickCounts(response.Embed, allPlays, processedPlays);

        if (deletion != null)
        {
            if (deletion == false)
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Warning ⚠️",
                    $"This will delete **{processedPlays.Count(c => c.PlaySource != PlaySource.LastFm)}** imported plays. \n" +
                    "This action can only be reversed by re-importing.");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm deletion", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.TrackDeleteConfirmed}-{capitalizedArtistName}——{capitalizedTrackName}");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
                response.Embed.AddField("Imports successfully deleted ✅",
                    $"Removed `{capitalizedTrackName}` by `{capitalizedArtistName}`");
                response.Components = null;
            }
        }
        else
        {
            if (newArtistName == null && newTrackName == null)
            {
                response.Components = new ComponentBuilder()
                    .WithButton("Edit track imports", style: ButtonStyle.Secondary,
                        customId:
                        $"{InteractionConstants.ImportModify.TrackRename}-{capitalizedArtistName}——{capitalizedTrackName}")
                    .WithButton("Delete imports", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.TrackDelete}-{capitalizedArtistName}——{capitalizedTrackName}");
            }
            else if (oldArtistName == null && oldTrackName == null)
            {
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.Embed.AddField("Warning ⚠️",
                    $"This will rename **{processedPlays.Count}** imported plays. \n" +
                    "This action can't be undone.");

                response.Components = new ComponentBuilder()
                    .WithButton("Confirm rename", style: ButtonStyle.Danger,
                        customId:
                        $"{InteractionConstants.ImportModify.TrackRenameConfirmed}-{capitalizedArtistName}——{capitalizedTrackName}——{newArtistName}——{newTrackName}");
            }
            else
            {
                response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
                response.Embed.AddField("Imports successfully edited ✅",
                    $"`{oldTrackName}` by `{oldArtistName}` to `{newTrackName}` by `{newArtistName}`");
                response.Embed.AddField("Note about future imports",
                    $"Usually when you imports duplicates will be filtered out. However, note that now that your imports are edited there might be duplicates when you import the same service again.");
                response.Components = null;
            }
        }

        return response;
    }

    private static void AddImportPickCounts(EmbedBuilder embed, ICollection<UserPlay> allPlays,
        ICollection<UserPlay> processedPlays)
    {
        var totalDescription = new StringBuilder();
        totalDescription.AppendLine($"- {allPlays.Count} total plays");
        if (allPlays.Any(c => c.PlaySource == PlaySource.LastFm))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm)} Last.fm scrobbles");
        }

        if (allPlays.Any(c => c.PlaySource == PlaySource.SpotifyImport))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport)} Spotify imports");
        }

        if (allPlays.Any(c => c.PlaySource == PlaySource.AppleMusicImport))
        {
            totalDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport)} Apple Music imports");
        }

        embed.AddField("Total playcounts - Including overlapping/duplicate plays", totalDescription.ToString());

        var processedDescription = new StringBuilder();
        processedDescription.AppendLine($"- {processedPlays.Count()} total plays");
        if (processedPlays.Any(c => c.PlaySource == PlaySource.LastFm))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.LastFm)} of those are Last.fm scrobbles");
        }

        if (processedPlays.Any(c => c.PlaySource == PlaySource.SpotifyImport))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport)} of those are Spotify imports");
        }

        if (processedPlays.Any(c => c.PlaySource == PlaySource.AppleMusicImport))
        {
            processedDescription.AppendLine(
                $"- {processedPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport)} of those are Apple Music imports");
        }

        embed.AddField("Final playcounts - Overlapping plays filtered", processedDescription.ToString());
    }
}
