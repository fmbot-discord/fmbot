using System;
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
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class ImportBuilders
{
    private readonly PlayService _playService;

    public ImportBuilders(PlayService playService)
    {
        this._playService = playService;
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
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink)
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

    public async Task<ResponseModel> GetSpotifyImportInstructions(ContextModel context, bool directToSlash = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

        response.Embed.WithTitle("Spotify import instructions");

        var description = new StringBuilder();

        description.AppendLine("### Requesting your data from Spotify");
        description.AppendLine("1. Go to your **[Spotify privacy settings](https://www.spotify.com/us/account/privacy/)**");
        description.AppendLine("2. Scroll down to \"Download your data\"");
        description.AppendLine("3. Select **Extended streaming history**");
        description.AppendLine("4. De-select the other options");
        description.AppendLine("5. Press request data");
        description.AppendLine("6. Confirm your data request through your email");
        description.AppendLine("7. Wait up to 30 days for Spotify to deliver your files");

        description.AppendLine("### Importing your data into .fmbot");
        description.AppendLine("1. Download the file Spotify provided");

        if (context.SlashCommand)
        {
            description.AppendLine("2. Use this command and add the `.zip` file as an attachment through the options");
        }
        else
        {
            description.AppendLine($"2. Use `/import Spotify` and add the `.zip` file as an attachment through the options");
        }

        description.AppendLine("3. Having issues? You can also attach each `.json` file separately");

        description.AppendLine("### Notes");
        description.AppendLine("- We filter out duplicates and skips, so don't worry about submitting the same file twice");
        description.AppendLine("- You can select what from your import you want to use with `/import manage`");

        var importedYears = await this.GetImportedYears(context.ContextUser.UserId, PlaySource.SpotifyImport);
        if (importedYears != null)
        {
            description.AppendLine("### Total imported Spotify plays");
            description.AppendLine(importedYears);
        }

        var footer = new StringBuilder();
        if (!context.SlashCommand || directToSlash)
        {
            footer.AppendLine("Do not share your import files publicly");
            footer.AppendLine("To start your import, use the slash command version of this command");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());

        response.Embed.WithDescription(description.ToString());

        response.Components = new ComponentBuilder()
            .WithButton("Spotify privacy page", style: ButtonStyle.Link, url: "https://www.spotify.com/us/account/privacy/");

        return response;
    }

    public async Task<ResponseModel> GetAppleMusicImportInstructions(ContextModel context, bool directToSlash = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        response.Embed.WithTitle("Apple Music import instructions");

        var description = new StringBuilder();

        description.AppendLine("### Requesting your data from Apple");
        description.AppendLine("1. Go to your **[Apple privacy settings](https://privacy.apple.com/)**");
        description.AppendLine("2. Sign in to your account");
        description.AppendLine("3. Click on **Request a copy of your data**");
        description.AppendLine("4. Select **Apple Media Services Information**");
        description.AppendLine("5. De-select the other options");
        description.AppendLine("6. Press **Continue**");
        description.AppendLine("7. Press **Complete request**");
        description.AppendLine("8. Wait up to 7 days for Apple to deliver your files");

        description.AppendLine("### Importing your data into .fmbot");
        description.AppendLine("1. Download the file Apple provided");

        if (context.SlashCommand)
        {
            description.AppendLine("2. Use this command and add the `.zip` file as an attachment through the options");
        }
        else
        {
            description.AppendLine($"2. Use `/import applemusic` and add the `.zip` file as an attachment through the options");
        }

        description.AppendLine("3. Having issues? You can also attach the `Apple Music Play Activity.csv` file separately");

        description.AppendLine("### Notes");
        description.AppendLine("- Apple provides their history data without artist names. We try to find these as best as possible based on the album and track name.");
        description.AppendLine("- You can select what from your import you want to use with `/import manage`");

        var importedYears = await this.GetImportedYears(context.ContextUser.UserId, PlaySource.AppleMusicImport);
        if (importedYears != null)
        {
            description.AppendLine("### Total imported Apple Music plays");
            description.AppendLine(importedYears);
        }

        var footer = new StringBuilder();
        if (!context.SlashCommand || directToSlash)
        {
            footer.AppendLine("Do not share your import files publicly");
            footer.AppendLine("To start your import, use the slash command version of this command");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());

        response.Embed.WithDescription(description.ToString());

        response.Components = new ComponentBuilder()
            .WithButton("Apple Data and Privacy", style: ButtonStyle.Link, url: "https://privacy.apple.com/");

        return response;
    }

    public async Task<string> GetImportedYears(int userId, PlaySource playSource)
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
                $"- **{playcount}** {StringExtensions.GetPlaysString(playcount)}");
        }

        return years.Length > 0 ? years.ToString() : null;
    }

      public async Task<ResponseModel> ImportModify(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var importSetting = new SelectMenuBuilder()
            .WithPlaceholder("Select modification")
            .WithCustomId(InteractionConstants.ImportModify)
            .WithMinValues(1)
            .WithMaxValues(1);

        var allPlays = await this._playService.GetAllUserPlays(userId, false);
        var hasImported = allPlays.Any(a =>
            a.PlaySource == PlaySource.SpotifyImport || a.PlaySource == PlaySource.AppleMusicImport);

        if (!hasImported && context.ContextUser.DataSource == DataSource.LastFm)
        {
            importSetting.IsDisabled = true;
        }

        foreach (var option in ((DataSource[])Enum.GetValues(typeof(DataSource))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(option);

            var active = context.ContextUser.DataSource == option;

            importSetting.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
        }

        response.Components = new ComponentBuilder().WithSelectMenu(importSetting);

        response.Embed.WithAuthor("Modify your .fmbot imports");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var importSource = "import data";
        if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport) &&
            allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Apple Music & Spotify";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
        {
            importSource = "Apple Music";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Spotify";
        }

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("Modify your imported .fmbot data with the options below.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("Please keep in mind that this only modifies imports that are stored in .fmbot. Importing in .fmbot works by combining imported plays together with Last.fm scrobbles.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("No Last.fm data can be changed or removed with this command.");

        embedDescription.AppendLine($"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm)} Last.fm scrobbles");
        embedDescription.AppendLine();

        embedDescription.AppendLine($"**Full Imports, then Last.fm**");
        embedDescription.AppendLine($"- Uses your full {importSource} history and adds Last.fm afterwards");
        embedDescription.AppendLine("- Plays from other music apps you scrobbled to Last.fm will not be included");

        if (!hasImported)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                "Run the `/import spotify` command to see how to request your data and to get started with imports. " +
                "After importing you'll be able to change these settings.");
        }
        else
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine($"**Total counts**");
            if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
            {
                embedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport)} imported Apple Music plays");
            }

            if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
            {
                embedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport)} imported Spotify plays");
            }

            embedDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm)} Last.fm scrobbles");

            var playResult =
                await this._playService.GetPlaysWithDataSource(userId, context.ContextUser.DataSource);
            embedDescription.AppendLine();
            embedDescription.AppendLine($"**Data in use with your selected mode**");
            embedDescription.Append(
                $"- {playResult.Count(c => c.PlaySource == PlaySource.SpotifyImport || c.PlaySource == PlaySource.AppleMusicImport)} imports + ");
            embedDescription.Append(
                $"{playResult.Count(c => c.PlaySource == PlaySource.LastFm)} scrobbles = ");
            embedDescription.Append($"{playResult.Count} plays");
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }
}
