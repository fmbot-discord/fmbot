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
using System.Xml.Linq;
using FMBot.Bot.Builders;
using Fergun.Interactive;

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

    [SlashCommand("spotify", "Import your Spotify history")]
    [UsernameSetRequired]
    public async Task SpotifyAsync(
        [Summary("file-1", "Spotify endsong.json file")] IAttachment attachment1,
        [Summary("file-2", "Spotify endsong.json file")] IAttachment attachment2 = null,
        [Summary("file-3", "Spotify endsong.json file")] IAttachment attachment3 = null,
        [Summary("file-4", "Spotify endsong.json file")] IAttachment attachment4 = null,
        [Summary("file-5", "Spotify endsong.json file")] IAttachment attachment5 = null,
        [Summary("file-6", "Spotify endsong.json file")] IAttachment attachment6 = null,
        [Summary("file-7", "Spotify endsong.json file")] IAttachment attachment7 = null,
        [Summary("file-8", "Spotify endsong.json file")] IAttachment attachment8 = null,
        [Summary("file-9", "Spotify endsong.json file")] IAttachment attachment9 = null,
        [Summary("file-10", "Spotify endsong.json file")] IAttachment attachment10 = null,
        [Summary("file-11", "Spotify endsong.json file")] IAttachment attachment11 = null,
        [Summary("file-12", "Spotify endsong.json file")] IAttachment attachment12 = null,
        [Summary("file-13", "Spotify endsong.json file")] IAttachment attachment13 = null,
        [Summary("file-14", "Spotify endsong.json file")] IAttachment attachment14 = null,
        [Summary("file-15", "Spotify endsong.json file")] IAttachment attachment15 = null,
        [Summary("file-16", "Spotify endsong.json file")] IAttachment attachment16 = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.UserType != UserType.Admin && contextUser.UserType != UserType.Owner)
        {
            await RespondAsync("Not available yet!");
            return;
        }

        var attachments = new List<IAttachment>
        {
            attachment1, attachment2, attachment3, attachment4, attachment5, attachment6,
            attachment7, attachment8, attachment9, attachment10, attachment11, attachment12,
            attachment13, attachment14, attachment15, attachment16
        };

        await DeferAsync();

        try
        {
            var embed = new EmbedBuilder();
            var description = new StringBuilder();

            embed.WithTitle("Importing Spotify into .fmbot.. (Beta)");
            embed.WithColor(DiscordConstants.SpotifyColorGreen);
            embed.WithDescription("- <a:loading:821676038102056991> Loading import files...");
            var message = await FollowupAsync(embed: embed.Build());

            var imports = await this._importService.HandleSpotifyFiles(attachments);

            if (!imports.success)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateImportEmbed(message, embed, description, $"- ❌ Invalid Spotify import file. Make sure you select the right files, for example `endsong_1.json`.", true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            else
            {
                await UpdateImportEmbed(message, embed, description, $"- **{imports.result.Count}** Spotify imports found");
            }

            var plays = await this._importService.SpotifyImportToUserPlays(contextUser.UserId, imports.result);
            await UpdateImportEmbed(message, embed, description, $"- **{plays.Count}** actual plays found");

            var playsWithoutDuplicates =
                await this._importService.RemoveDuplicateSpotifyImports(contextUser.UserId, plays);
            await UpdateImportEmbed(message, embed, description, $"- **{playsWithoutDuplicates.Count}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await this._importService.InsertImportPlays(playsWithoutDuplicates);
                await UpdateImportEmbed(message, embed, description, $"- Added plays to database");
            }

            await this._indexService.RecalculateTopLists(contextUser);
            await UpdateImportEmbed(message, embed, description, $"- Recalculated top lists");

            await this._importService.UpdateExistingPlays(contextUser.UserId);

            var files = new StringBuilder();
            foreach (var attachment in attachments
                         .Where(w => w != null)
                         .OrderBy(o => o.Filename))
            {
                files.AppendLine($"`{attachment.Filename}`");
            }

            embed.AddField("Processed files", files.ToString());

            var years = new StringBuilder();
            var allPlays = await this._playService
                .GetAllUserPlays(contextUser.UserId);

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
            if (years.Length > 0)
            {
                embed.AddField("Total imported plays", years.ToString());
            }

            await UpdateImportEmbed(message, embed, description, $"- ✅ Import complete", true);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private static async Task UpdateImportEmbed(IUserMessage msg, EmbedBuilder embed, StringBuilder builder, string lineToAdd, bool lastLine = false)
    {
        const string loadingLine = "- <a:loading:821676038102056991> Processing...";
        builder.Replace($"\r\n{loadingLine}", "");
        builder.AppendLine(lineToAdd);

        if (!lastLine)
        {
            builder.AppendLine(loadingLine);
        }

        embed.WithDescription(builder.ToString());

        await msg.ModifyAsync(m =>
        {
            m.Embed = embed.Build();
        });
    }

    [SlashCommand("manage", "Manage your import settings")]
    [UsernameSetRequired]
    public async Task ManageImportAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.UserType != UserType.Admin && contextUser.UserType != UserType.Owner)
        {
            await RespondAsync("Not available yet!");
            return;
        }

        try
        {
            var hasImported = await this._importService.HasImported(contextUser.UserId);
            var response = UserBuilder.ImportMode(new ContextModel(this.Context, contextUser), hasImported);

            await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
