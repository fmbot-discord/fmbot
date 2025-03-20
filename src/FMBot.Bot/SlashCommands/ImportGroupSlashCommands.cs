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
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using Fergun.Interactive;
using FMBot.Domain.Extensions;

namespace FMBot.Bot.SlashCommands;

[Group("import", "Manage your data imports")]
public class ImportGroupSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly ImportService _importService;
    private readonly PlayService _playService;
    private readonly IndexService _indexService;
    private readonly ImportBuilders _importBuilders;
    private readonly SupporterService _supporterService;
    private readonly UserBuilder _userBuilder;
    private InteractiveService Interactivity { get; }

    public ImportGroupSlashCommands(UserService userService,
        IDataSourceFactory dataSourceFactory,
        ImportService importService,
        PlayService playService,
        IndexService indexService,
        InteractiveService interactivity,
        ImportBuilders importBuilders,
        SupporterService supporterService,
        UserBuilder userBuilder)
    {
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
        this._importService = importService;
        this._playService = playService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._importBuilders = importBuilders;
        this._supporterService = supporterService;
        this._userBuilder = userBuilder;
    }

    private const string SpotifyFileDescription = "Spotify history package (.zip) or history files (.json) ";

    [SlashCommand("spotify", "⭐ Import your Spotify history into .fmbot")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task SpotifyAsync(
        [Summary("file-1", SpotifyFileDescription)]
        IAttachment attachment1 = null,
        [Summary("file-2", SpotifyFileDescription)]
        IAttachment attachment2 = null,
        [Summary("file-3", SpotifyFileDescription)]
        IAttachment attachment3 = null,
        [Summary("file-4", SpotifyFileDescription)]
        IAttachment attachment4 = null,
        [Summary("file-5", SpotifyFileDescription)]
        IAttachment attachment5 = null,
        [Summary("file-6", SpotifyFileDescription)]
        IAttachment attachment6 = null,
        [Summary("file-7", SpotifyFileDescription)]
        IAttachment attachment7 = null,
        [Summary("file-8", SpotifyFileDescription)]
        IAttachment attachment8 = null,
        [Summary("file-9", SpotifyFileDescription)]
        IAttachment attachment9 = null,
        [Summary("file-10", SpotifyFileDescription)]
        IAttachment attachment10 = null,
        [Summary("file-11", SpotifyFileDescription)]
        IAttachment attachment11 = null,
        [Summary("file-12", SpotifyFileDescription)]
        IAttachment attachment12 = null,
        [Summary("file-13", SpotifyFileDescription)]
        IAttachment attachment13 = null,
        [Summary("file-14", SpotifyFileDescription)]
        IAttachment attachment14 = null,
        [Summary("file-15", SpotifyFileDescription)]
        IAttachment attachment15 = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await this._supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            this._userService.RemoveUserFromCache(contextUser);
            contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        var attachments = new List<IAttachment>
        {
            attachment1, attachment2, attachment3, attachment4, attachment5, attachment6,
            attachment7, attachment8, attachment9, attachment10, attachment11, attachment12,
            attachment13, attachment14, attachment15
        };

        var noAttachments = attachments.All(a => a == null);
        attachments = attachments.Where(w => w != null).ToList();

        var description = new StringBuilder();
        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);

        if (noAttachments)
        {
            var instructionResponse =
                await this._importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, instructionResponse);
            this.Context.LogCommandUsed(instructionResponse.CommandResponse);
            return;
        }

        await DeferAsync(ephemeral: noAttachments);

        embed.AddField($"{DiscordConstants.Spotify} Importing history into .fmbot..",
            $"- {DiscordConstants.Loading} Loading import files...");
        var message = await FollowupAsync(embed: embed.Build());

        try
        {
            var imports = await this._importService.HandleSpotifyFiles(contextUser, attachments);

            if (imports.status == ImportStatus.UnknownFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import file. Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.status == ImportStatus.WrongPackageFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import files. You have uploaded the wrong Spotify data package.\n\n" +
                    $"We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. Instead you have uploaded the 'Account data' package.",
                    true,
                    image: "https://fmbot.xyz/img/bot/import-spotify-instructions.png",
                    components: new ComponentBuilder().WithButton("Spotify privacy page", style: ButtonStyle.Link,
                        url: "https://www.spotify.com/us/account/privacy/"));
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.result == null || imports.result.Count == 0 || imports.result.All(a => a.MsPlayed == 0))
            {
                if (attachments != null &&
                    attachments.Any(a => a.Filename != null) &&
                    attachments.Any(a => a.Filename.ToLower().Contains("streaminghistory")))
                {
                    embed.WithColor(DiscordConstants.WarningColorOrange);
                    await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                        $"❌ Invalid Spotify import file. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package.\n\n" +
                        $"The files should have names like `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                        $"The right files can take some more time to get, but actually contain your full Spotify history. Sorry for the inconvenience.",
                        true);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import file (contains no plays). Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                    $"If your `.zip` contains files like `Userdata.json` or `Identity.json` its the wrong package. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. ",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                $"- **{imports.result.Count.Format(numberFormat)}** Spotify imports found");

            var plays = await this._importService.SpotifyImportToUserPlays(contextUser, imports.result);
            await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                $"- **{plays.Count.Format(numberFormat)}** actual plays found");

            var playsWithoutDuplicates =
                await this._importService.RemoveDuplicateImports(contextUser.UserId, plays);
            await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                $"- **{playsWithoutDuplicates.Count.Format(numberFormat)}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await this._importService.InsertImportPlays(contextUser, playsWithoutDuplicates);
                await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                    $"- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    var userHasImportedLastfm = await this._playService.UserHasImportedLastFm(contextUser.UserId);

                    if (userHasImportedLastfm)
                    {
                        await this._userService.SetDataSource(contextUser, DataSource.FullImportThenLastFm);
                    }
                    else
                    {
                        await this._userService.SetDataSource(contextUser, DataSource.ImportThenFullLastFm);
                    }

                    await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                        $"- Updated import setting");
                }
            }

            if (contextUser.DataSource != DataSource.LastFm)
            {
                await this._indexService.RecalculateTopLists(contextUser);
                await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                    $"- Refreshed top list cache");
            }

            await this._importService.UpdateExistingScrobbleSource(contextUser);

            var years = await this._importBuilders.GetImportedYears(contextUser.UserId, PlaySource.SpotifyImport,
                numberFormat);
            if (years.Length > 0)
            {
                embed.AddField("<:fmbot_importing:1131511469096312914> All imported Spotify plays", years, true);
            }

            contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importActivated = new StringBuilder();
            var importSetting = new StringBuilder();

            switch (contextUser.DataSource)
            {
                case DataSource.LastFm:
                    importActivated.AppendLine(
                        "Your import setting is currently still set to just Last.fm, so imports will not be used. You can change this manually with the button below.");
                    break;
                case DataSource.FullImportThenLastFm:
                    importActivated.AppendLine(
                        "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                    importSetting.AppendLine(
                        "Your import setting has been set to **Full imports, then Last.fm**. This uses your full Spotify history and adds your Last.fm scrobbles afterwards.");
                    break;
                case DataSource.ImportThenFullLastFm:
                    importActivated.AppendLine(
                        "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                    importSetting.AppendLine(
                        "Your import setting has been set to **Imports, then full Last.fm**. This uses your Spotify history up until you started using Last.fm.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (importActivated.Length > 0)
            {
                embed.AddField("✅ Importing service activated", importActivated);
            }

            if (importSetting.Length > 0)
            {
                embed.AddField("⚙️ Current import setting", importSetting);
            }

            var components = new ComponentBuilder()
                .WithButton("View your stats", $"{InteractionConstants.RecapAlltime}-{contextUser.UserId}",
                    style: ButtonStyle.Primary)
                .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary);

            embed.WithColor(DiscordConstants.SpotifyColorGreen);
            await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description, $"- Import complete!", true,
                components);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await UpdateSpotifyImportEmbed(this.Context.Interaction, embed, description,
                $"- ❌ Sorry, an internal error occured. Please try again later, or open a help thread on [our server](https://discord.gg/fmbot).",
                true);
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    [SlashCommand("applemusic", "⭐ Import your Apple Music history into .fmbot")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task AppleMusicAsync(
        [Summary("file", "'Apple Media Services information.zip' or 'Apple Music Play Activity.csv' file")]
        IAttachment attachment = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await this._supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            this._userService.RemoveUserFromCache(contextUser);
            contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        var description = new StringBuilder();
        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);

        if (attachment == null)
        {
            var instructionResponse =
                await this._importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, instructionResponse);
            this.Context.LogCommandUsed(instructionResponse.CommandResponse);
            return;
        }

        await DeferAsync(ephemeral: false);

        embed.AddField($"{DiscordConstants.AppleMusic} Importing history into .fmbot..",
            $"- {DiscordConstants.Loading} Loading import files...");

        var message = await FollowupAsync(embed: embed.Build());

        try
        {
            var imports = await this._importService.HandleAppleMusicFiles(contextUser, attachment);

            if (imports.status == ImportStatus.UnknownFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                    $"❌ Invalid Apple Music import file, or something went wrong.\n\n" +
                    $"If you've uploaded a `.zip` file you can also try to find the `Apple Music Play Activity.csv` inside the .zip and attach that instead.\n\n" +
                    $"You can also open a help thread on [our server](https://discord.gg/fmbot).", true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.status == ImportStatus.WrongCsvFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                    $"❌ We couldn't read the `.csv` file that was provided.\n\n" +
                    $"We can only read a `Apple Music Play Activity.csv` file. Other files do not contain the data required for importing.\n\n" +
                    $"Still having issues? You can also open a help thread on [our server](https://discord.gg/fmbot).",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- **{imports.result.Count.Format(numberFormat)}** Apple Music imports found");

            var importsWithArtist = await this._importService.AppleMusicImportAddArtists(contextUser, imports.result);
            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- **{importsWithArtist.matchFoundPercentage}** of artist names found for imports");
            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- **{importsWithArtist.userPlays.Count(c => !string.IsNullOrWhiteSpace(c.ArtistName))}** with artist names");

            var plays = ImportService.AppleMusicImportsToValidUserPlays(contextUser, importsWithArtist.userPlays);
            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- **{plays.Count.Format(numberFormat)}** actual plays found");

            var playsWithoutDuplicates =
                await this._importService.RemoveDuplicateImports(contextUser.UserId, plays);
            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- **{playsWithoutDuplicates.Count.Format(numberFormat)}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await this._importService.InsertImportPlays(contextUser, playsWithoutDuplicates);
                await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                    $"- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    var userHasImportedLastfm = await this._playService.UserHasImportedLastFm(contextUser.UserId);

                    if (userHasImportedLastfm)
                    {
                        await this._userService.SetDataSource(contextUser, DataSource.FullImportThenLastFm);
                    }
                    else
                    {
                        await this._userService.SetDataSource(contextUser, DataSource.ImportThenFullLastFm);
                    }

                    await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                        $"- Updated import setting");
                }
            }

            if (contextUser.DataSource != DataSource.LastFm)
            {
                await this._indexService.RecalculateTopLists(contextUser);
                await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                    $"- Refreshed top list cache");
            }

            await this._importService.UpdateExistingScrobbleSource(contextUser);

            var years = await this._importBuilders.GetImportedYears(contextUser.UserId, PlaySource.AppleMusicImport,
                numberFormat);
            if (years.Length > 0)
            {
                embed.AddField("<:fmbot_importing:1131511469096312914> All imported Apple Music plays", years, true);
            }

            contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var importActivated = new StringBuilder();
            var importSetting = new StringBuilder();

            switch (contextUser.DataSource)
            {
                case DataSource.LastFm:
                    importActivated.AppendLine(
                        "Your import setting is currently still set to just Last.fm, so imports will not be used. You can change this manually with the button below.");
                    break;
                case DataSource.FullImportThenLastFm:
                    importActivated.AppendLine(
                        "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                    importSetting.AppendLine(
                        "Your import setting has been set to **Full imports, then Last.fm**. This uses your full Apple Music history and adds your Last.fm scrobbles afterwards.");
                    break;
                case DataSource.ImportThenFullLastFm:
                    importActivated.AppendLine(
                        "With this service all playcounts and history in the bot will consist of your imports combined with your Last.fm history. The bot re-calculates this every time you run a command, all while still responding quickly.");

                    importSetting.AppendLine(
                        "Your import setting has been set to **Imports, then full Last.fm**. This uses your Apple Music history up until you started using Last.fm.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (importActivated.Length > 0)
            {
                embed.AddField("✅ Importing service activated", importActivated);
            }

            if (importSetting.Length > 0)
            {
                embed.AddField("⚙️ Current import setting", importSetting);
            }

            embed.WithColor(DiscordConstants.AppleMusicRed);

            var components = new ComponentBuilder()
                .WithButton("View your stats", $"{InteractionConstants.RecapAlltime}-{contextUser.UserId}",
                    style: ButtonStyle.Primary)
                .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary);

            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description, $"- Import complete!", true,
                components);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await UpdateAppleMusicImportEmbed(this.Context.Interaction, embed, description,
                $"- ❌ Sorry, an internal error occured. Please try again later, or open a help thread on [our server](https://discord.gg/fmbot).",
                true);
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    private static async Task UpdateSpotifyImportEmbed(IDiscordInteraction interaction, EmbedBuilder embed,
        StringBuilder builder,
        string lineToAdd, bool lastLine = false, ComponentBuilder components = null, string image = null)
    {
        await UpdateImportEmbed(interaction, embed, builder, lineToAdd, lastLine, components, image,
            PlaySource.SpotifyImport);
    }

    private static async Task UpdateAppleMusicImportEmbed(IDiscordInteraction interaction, EmbedBuilder embed,
        StringBuilder builder,
        string lineToAdd, bool lastLine = false, ComponentBuilder components = null, string image = null)
    {
        await UpdateImportEmbed(interaction, embed, builder, lineToAdd, lastLine, components, image,
            PlaySource.AppleMusicImport);
    }

    private static async Task UpdateImportEmbed(IDiscordInteraction interaction, EmbedBuilder embed,
        StringBuilder builder,
        string lineToAdd, bool lastLine = false, ComponentBuilder components = null, string image = null,
        PlaySource playSource = PlaySource.SpotifyImport)
    {
        builder.AppendLine(lineToAdd);

        const string loadingLine = $"- {DiscordConstants.Loading} Processing...";

        var title = playSource == PlaySource.SpotifyImport
            ? $"{DiscordConstants.Spotify} Importing history into .fmbot.."
            : $"{DiscordConstants.AppleMusic} Importing history into .fmbot..";

        var index = embed.Fields.FindIndex(f => f.Name == title);
        embed.Fields[index] = new EmbedFieldBuilder()
            .WithName(title)
            .WithValue(builder + (lastLine ? null : loadingLine))
            .WithIsInline(true);

        if (image != null)
        {
            embed.WithImageUrl(image);
        }

        await interaction.ModifyOriginalResponseAsync(m =>
        {
            m.Embed = embed.Build();
            m.Components = components?.Build();
        });
    }

    [SlashCommand("manage", "⭐ Manage your imports and configure how they are used")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task ManageImportAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        await DeferAsync(ephemeral: true);

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

    [SlashCommand("modify", "⭐ Edit and delete artists, albums and tracks in your .fmbot imports")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task ModifyImportAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        await DeferAsync(ephemeral: true);

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedBuilder()
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithDescription("Check your DMs to continue with modifying your .fmbot imports.");

            await RespondAsync(embed: serverEmbed.Build(), ephemeral: true);
        }
        else if (this.Context.Channel != null)
        {
            _ = this.Context.Channel.TriggerTypingAsync();
        }

        try
        {
            var response =
                await this._importBuilders.ImportModify(new ContextModel(this.Context, contextUser), contextUser.UserId);
            await this.Context.User.SendMessageAsync("", false, response.Embed.Build(),
                components: response.Components.Build());
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
