using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
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
            response.Embed.WithDescription($"Only supporters can import and use their Spotify history.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink)
                .WithButton("Import info", style: ButtonStyle.Link, url: "https://fmbot.xyz/importing/");
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public async Task<ResponseModel> GetImportInstructions(ContextModel context)
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
        description.AppendLine("- Discord mobile breaks `.json` files, use the full `.zip` instead.");

        var importedYears = await this.GetImportedYears(context.ContextUser.UserId);
        if (importedYears != null)
        {
            description.AppendLine("### Total imported plays");
            description.AppendLine(importedYears);
        }

        var footer = new StringBuilder();
        if (!context.SlashCommand)
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

    public async Task<string> GetImportedYears(int userId)
    {
        var years = new StringBuilder();
        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);

        var yearGroups = allPlays
            .Where(w => w.PlaySource == PlaySource.SpotifyImport)
            .OrderByDescending(o => o.TimePlayed)
            .GroupBy(g => g.TimePlayed.Year);

        foreach (var year in yearGroups)
        {
            years.AppendLine(
                $"**`{year.Key}`** " +
                $"- **{year.Count()}** plays");
        }

        return years.Length > 0 ? years.ToString() : null;
    }
}
