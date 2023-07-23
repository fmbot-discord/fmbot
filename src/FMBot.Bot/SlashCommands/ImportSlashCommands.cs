using Discord;
using System.Collections.Generic;
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
using FMBot.Domain.Models;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using Fergun.Interactive;
using FMBot.Domain;

namespace FMBot.Bot.SlashCommands;

[Group("import", "Manage your data imports")]
public class ImportSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly ImportService _importService;
    private readonly PlayService _playService;
    private readonly IIndexService _indexService;
    private readonly UserBuilder _userBuilder;
    private InteractiveService Interactivity { get; }

    public ImportSlashCommands(UserService userService,
        IDataSourceFactory dataSourceFactory,
        ImportService importService,
        PlayService playService,
        IIndexService indexService,
        UserBuilder userBuilder,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
        this._importService = importService;
        this._playService = playService;
        this._indexService = indexService;
        this._userBuilder = userBuilder;
        this.Interactivity = interactivity;
    }

    private const string SpotifyFileDescription = "Spotify history package (.zip) or history files (.json) ";

    [SlashCommand("spotify", "Import your Spotify history (Beta)")]
    [UsernameSetRequired]
    public async Task SpotifyAsync(
        [Summary("file-1", SpotifyFileDescription)] IAttachment attachment1 = null,
        [Summary("file-2", SpotifyFileDescription)] IAttachment attachment2 = null,
        [Summary("file-3", SpotifyFileDescription)] IAttachment attachment3 = null,
        [Summary("file-4", SpotifyFileDescription)] IAttachment attachment4 = null,
        [Summary("file-5", SpotifyFileDescription)] IAttachment attachment5 = null,
        [Summary("file-6", SpotifyFileDescription)] IAttachment attachment6 = null,
        [Summary("file-7", SpotifyFileDescription)] IAttachment attachment7 = null,
        [Summary("file-8", SpotifyFileDescription)] IAttachment attachment8 = null,
        [Summary("file-9", SpotifyFileDescription)] IAttachment attachment9 = null,
        [Summary("file-10", SpotifyFileDescription)] IAttachment attachment10 = null,
        [Summary("file-11", SpotifyFileDescription)] IAttachment attachment11 = null,
        [Summary("file-12", SpotifyFileDescription)] IAttachment attachment12 = null,
        [Summary("file-13", SpotifyFileDescription)] IAttachment attachment13 = null,
        [Summary("file-14", SpotifyFileDescription)] IAttachment attachment14 = null,
        [Summary("file-15", SpotifyFileDescription)] IAttachment attachment15 = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var embed = new EmbedBuilder();

        if (contextUser.UserType == UserType.User)
        {
            embed.WithDescription($"Only supporters can import their Spotify history into .fmbot.");

            var components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);

            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(embed: embed.Build(), components: components.Build());

            this.Context.LogCommandUsed(CommandResponse.SupporterRequired);
            return;
        }

        var attachments = new List<IAttachment>
        {
            attachment1, attachment2, attachment3, attachment4, attachment5, attachment6,
            attachment7, attachment8, attachment9, attachment10, attachment11, attachment12,
            attachment13, attachment14, attachment15
        };

        var noAttachments = attachments.All(a => a == null);
        await DeferAsync(ephemeral: noAttachments);

        var description = new StringBuilder();
        embed.WithColor(DiscordConstants.SpotifyColorGreen);

        if (noAttachments)
        {
            embed.WithTitle("Spotify import instructions");

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
            description.AppendLine("2. Use this command and add the `.zip` file as an attachment through the options");
            description.AppendLine("3. Having issues? You can also attach each `.json` file separately");

            description.AppendLine("### Notes");
            description.AppendLine("- We filter out duplicates, so don't worry about submitting the same file twice");
            description.AppendLine("- Spotify files includes plays that you skipped quickly, we filter those out as well");
            description.AppendLine("- You can select what from your import you want to use with `/import manage`");
            description.AppendLine("- Discord mobile currently has an issue where it corrupts any `.json` file you send through it. Attach the `.zip` instead, or try using Discord desktop");

            var years = await this.GetImportedYears(contextUser.UserId);
            if (years.Length > 0)
            {
                embed.AddField("Total imported plays", years);
            }

            embed.WithDescription(description.ToString());
            embed.WithFooter("Spotify importing is currently in beta. Please report any issues you encounter");

            var components = new ComponentBuilder()
                .WithButton("Spotify privacy settings", style: ButtonStyle.Link, url: "https://www.spotify.com/us/account/privacy/");

            this.Context.LogCommandUsed(CommandResponse.Help);
            await this.FollowupAsync(embed: embed.Build(), ephemeral: noAttachments, components: components.Build());
            return;
        }

        try
        {
            embed.WithTitle("Importing Spotify into .fmbot.. (Beta)");
            embed.WithDescription("- <a:loading:821676038102056991> Loading import files...");
            var message = await FollowupAsync(embed: embed.Build());

            var imports = await this._importService.HandleSpotifyFiles(attachments);

            if (!imports.success)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateImportEmbed(message, embed, description, $"- ❌ Invalid Spotify import file. Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`." , true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.result == null || imports.result.Count == 0 || imports.result.All(a => a.MsPlayed == 0))
            {
                if (attachments != null && attachments.Any(a => a.Filename != null) && attachments.Any(a => a.Filename.ToLower().Contains("streaminghistory")))
                {
                    embed.WithColor(DiscordConstants.WarningColorOrange);
                    await UpdateImportEmbed(message, embed, description, $"❌ Invalid Spotify import file. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package.\n\n" +
                                                                         $"The files should have names like `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                                                                         $"The right files can take some more time to get, but actually contain your full Spotify history. Sorry for the inconvenience.", true);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateImportEmbed(message, embed, description, $"- ❌ Invalid Spotify import file (contains no plays). Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                                                                     $"The Discord mobile app currently empties all `.json` files you send through it. We've reported this to them, try using Discord on desktop in the meantime.", true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await UpdateImportEmbed(message, embed, description, $"- **{imports.result.Count}** Spotify imports found");

            var plays = await this._importService.SpotifyImportToUserPlays(contextUser.UserId, imports.result);
            await UpdateImportEmbed(message, embed, description, $"- **{plays.Count}** actual plays found");

            var playsWithoutDuplicates =
                await this._importService.RemoveDuplicateSpotifyImports(contextUser.UserId, plays);
            await UpdateImportEmbed(message, embed, description, $"- **{playsWithoutDuplicates.Count}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await this._importService.InsertImportPlays(playsWithoutDuplicates);
                await UpdateImportEmbed(message, embed, description, $"- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    await this._userService.SetDataSource(contextUser, DataSource.FullSpotifyThenLastFm);
                }
            }

            await this._indexService.RecalculateTopLists(contextUser);
            await UpdateImportEmbed(message, embed, description, $"- Recalculated top lists");

            await this._importService.UpdateExistingPlays(contextUser.UserId);

            var files = new StringBuilder();
            foreach (var attachment in attachments
                         .Where(w => w != null)
                         .OrderBy(o => o.Filename)
                         .GroupBy(g => g.Filename))
            {
                files.AppendLine($"`{attachment.First().Filename}`");
            }

            embed.AddField("Processed files", files.ToString());

            var years = await this.GetImportedYears(contextUser.UserId);
            if (years.Length > 0)
            {
                embed.AddField("Total imported plays", years);
            }

            var components = new ComponentBuilder()
                .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary);

            await UpdateImportEmbed(message, embed, description, $"- ✅ Import complete", true, components);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private static async Task UpdateImportEmbed(IUserMessage msg, EmbedBuilder embed, StringBuilder builder, string lineToAdd, bool lastLine = false, ComponentBuilder components = null)
    {
        builder.AppendLine(lineToAdd);

        const string loadingLine = "- <a:loading:821676038102056991> Processing...";

        embed.WithDescription(builder + (lastLine ? null : loadingLine));

        await msg.ModifyAsync(m =>
        {
            m.Embed = embed.Build();
            m.Components = components?.Build();
        });
    }

    private async Task<string> GetImportedYears(int userId)
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

        return years.ToString();
    }

    [SlashCommand("manage", "Manage your import settings (Beta)")]
    [UsernameSetRequired]
    public async Task ManageImportAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.UserType == UserType.User)
        {
            var embed = new EmbedBuilder();
            embed.WithDescription($"Only supporters can import their Spotify history into .fmbot.");

            var components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);

            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(embed: embed.Build(), components: components.Build());

            this.Context.LogCommandUsed(CommandResponse.SupporterRequired);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var hasImported = await this._importService.HasImported(contextUser.UserId);
            var response = UserBuilder.ImportMode(new ContextModel(this.Context, contextUser), hasImported);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
