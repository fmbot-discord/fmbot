using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using FMBot.Domain.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("import", "Manage your data imports",
    Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
    IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
public class ImportGroupSlashCommands(
    UserService userService,
    ImportService importService,
    PlayService playService,
    IndexService indexService,
    InteractiveService interactivity,
    ImportBuilders importBuilders,
    SupporterService supporterService,
    UserBuilder userBuilder)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    private const string SpotifyFileDescription = "Spotify history package (.zip) or history files (.json) ";

    [SubSlashCommand("spotify", "⭐ Import your Spotify history into .fmbot")]
    [UsernameSetRequired]
    public async Task SpotifyAsync(
        [SlashCommandParameter(Name = "file-1", Description = SpotifyFileDescription)]
        Attachment attachment1 = null,
        [SlashCommandParameter(Name = "file-2", Description = SpotifyFileDescription)]
        Attachment attachment2 = null,
        [SlashCommandParameter(Name = "file-3", Description = SpotifyFileDescription)]
        Attachment attachment3 = null,
        [SlashCommandParameter(Name = "file-4", Description = SpotifyFileDescription)]
        Attachment attachment4 = null,
        [SlashCommandParameter(Name = "file-5", Description = SpotifyFileDescription)]
        Attachment attachment5 = null,
        [SlashCommandParameter(Name = "file-6", Description = SpotifyFileDescription)]
        Attachment attachment6 = null,
        [SlashCommandParameter(Name = "file-7", Description = SpotifyFileDescription)]
        Attachment attachment7 = null,
        [SlashCommandParameter(Name = "file-8", Description = SpotifyFileDescription)]
        Attachment attachment8 = null,
        [SlashCommandParameter(Name = "file-9", Description = SpotifyFileDescription)]
        Attachment attachment9 = null,
        [SlashCommandParameter(Name = "file-10", Description = SpotifyFileDescription)]
        Attachment attachment10 = null,
        [SlashCommandParameter(Name = "file-11", Description = SpotifyFileDescription)]
        Attachment attachment11 = null,
        [SlashCommandParameter(Name = "file-12", Description = SpotifyFileDescription)]
        Attachment attachment12 = null,
        [SlashCommandParameter(Name = "file-13", Description = SpotifyFileDescription)]
        Attachment attachment13 = null,
        [SlashCommandParameter(Name = "file-14", Description = SpotifyFileDescription)]
        Attachment attachment14 = null,
        [SlashCommandParameter(Name = "file-15", Description = SpotifyFileDescription)]
        Attachment attachment15 = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            userService.RemoveUserFromCache(contextUser);
            contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        var attachments = new List<Attachment>
        {
            attachment1, attachment2, attachment3, attachment4, attachment5, attachment6,
            attachment7, attachment8, attachment9, attachment10, attachment11, attachment12,
            attachment13, attachment14, attachment15
        };

        var noAttachments = attachments.All(a => a == null);
        attachments = attachments.Where(w => w != null).ToList();

        var description = new StringBuilder();
        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);

        if (noAttachments)
        {
            var instructionResponse =
                await importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, instructionResponse);
            this.Context.LogCommandUsed(instructionResponse.CommandResponse);
            return;
        }

        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(noAttachments ? MessageFlags.Ephemeral : default));

        embed.AddField(
            $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} Importing history into .fmbot..",
            $"- {EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Loading import files...");
        var message = await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithEmbeds([embed]));

        try
        {
            var imports = await importService.HandleSpotifyFiles(contextUser, attachments);

            if (imports.status == ImportStatus.UnknownFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import file. Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.status == ImportStatus.WrongPackageFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import files. You have uploaded the wrong Spotify data package.\n\n" +
                    $"We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. Instead you have uploaded the 'Account data' package.",
                    true,
                    image: "https://fm.bot/img/bot/import-spotify-instructions.png",
                    components: new ActionRowProperties().WithButton("Spotify privacy page",
                        url: "https://www.spotify.com/us/account/privacy/"));
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.result == null || imports.result.Count == 0 || imports.result.All(a => a.MsPlayed == 0))
            {
                if (attachments != null &&
                    attachments.Any(a => a.FileName != null) &&
                    attachments.Any(a => a.FileName.ToLower().Contains("streaminghistory")))
                {
                    embed.WithColor(DiscordConstants.WarningColorOrange);
                    await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                        $"❌ Invalid Spotify import file. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package.\n\n" +
                        $"The files should have names like `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                        $"The right files can take some more time to get, but actually contain your full Spotify history. Sorry for the inconvenience.",
                        true);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"❌ Invalid Spotify import file (contains no plays). Make sure you select the right files, for example `my_spotify_data.zip` or `Streaming_History_Audio_x.json`.\n\n" +
                    $"If your `.zip` contains files like `Userdata.json` or `Identity.json` its the wrong package. We can only process files that are from the ['Extended Streaming History'](https://www.spotify.com/us/account/privacy/) package. ",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{imports.result.Count.Format(numberFormat)}** Spotify imports found");

            var plays = await importService.SpotifyImportToUserPlays(contextUser, imports.result);
            await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{plays.Count.Format(numberFormat)}** actual plays found");

            var playsWithoutDuplicates =
                await importService.RemoveDuplicateImports(contextUser.UserId, plays);
            await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{playsWithoutDuplicates.Count.Format(numberFormat)}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await importService.InsertImportPlays(contextUser, playsWithoutDuplicates);
                await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    var userHasImportedLastfm = await playService.UserHasImportedLastFm(contextUser.UserId);

                    if (userHasImportedLastfm)
                    {
                        await userService.SetDataSource(contextUser, DataSource.FullImportThenLastFm);
                    }
                    else
                    {
                        await userService.SetDataSource(contextUser, DataSource.ImportThenFullLastFm);
                    }

                    await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                        $"- Updated import setting");
                }
            }

            if (contextUser.DataSource != DataSource.LastFm)
            {
                await indexService.RecalculateTopLists(contextUser);
                await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"- Refreshed top list cache");
            }

            await importService.UpdateExistingScrobbleSource(contextUser);

            var years = await importBuilders.GetImportedYears(contextUser.UserId, PlaySource.SpotifyImport,
                numberFormat);
            if (years is { Length: > 0 })
            {
                embed.AddField("<:fmbot_importing:1131511469096312914> All imported Spotify plays", years, true);
            }

            contextUser = await userService.GetUserSettingsAsync(this.Context.User);

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
                embed.AddField("✅ Importing service activated", importActivated.ToString());
            }

            if (importSetting.Length > 0)
            {
                embed.AddField("⚙️ Current import setting", importSetting.ToString());
            }

            var components = new ActionRowProperties()
                .WithButton("View your stats", $"{InteractionConstants.RecapAlltime}:{contextUser.UserId}",
                    style: ButtonStyle.Primary)
                .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary);

            embed.WithColor(DiscordConstants.SpotifyColorGreen);
            await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description, $"- Import complete!", true,
                components);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await UpdateSpotifyImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- ❌ Sorry, an internal error occured. Please try again later, or open a help thread on [our server](https://discord.gg/fmbot).",
                true);
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    [SubSlashCommand("applemusic", "⭐ Import your Apple Music history into .fmbot")]
    [UsernameSetRequired]
    public async Task AppleMusicAsync(
        [SlashCommandParameter(Name = "file",
            Description = "'Apple Media Services information.zip' or 'Apple Music Play Activity.csv' file")]
        Attachment attachment = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var numberFormat = contextUser.NumberFormat ?? NumberFormat.NoSeparator;

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            userService.RemoveUserFromCache(contextUser);
            contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        var description = new StringBuilder();
        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);

        if (attachment == null)
        {
            var instructionResponse =
                await importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, instructionResponse);
            this.Context.LogCommandUsed(instructionResponse.CommandResponse);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        embed.AddField(
            $"{EmojiProperties.Custom(DiscordConstants.AppleMusic).ToDiscordString("apple_music")} Importing history into .fmbot..",
            $"- {EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Loading import files...");

        var message = await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithEmbeds([embed]));

        try
        {
            var imports = await importService.HandleAppleMusicFiles(contextUser, attachment);

            if (imports.status == ImportStatus.UnknownFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"❌ Invalid Apple Music import file, or something went wrong.\n\n" +
                    $"If you've uploaded a `.zip` file you can also try to find the `Apple Music Play Activity.csv` inside the .zip and attach that instead.\n\n" +
                    $"You can also open a help thread on [our server](https://discord.gg/fmbot).", true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (imports.status == ImportStatus.WrongCsvFailure)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"❌ We couldn't read the `.csv` file that was provided.\n\n" +
                    $"We can only read a `Apple Music Play Activity.csv` file. Other files do not contain the data required for importing.\n\n" +
                    $"Still having issues? You can also open a help thread on [our server](https://discord.gg/fmbot).",
                    true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{imports.result.Count.Format(numberFormat)}** Apple Music imports found");

            var importsWithArtist = await importService.AppleMusicImportAddArtists(contextUser, imports.result);
            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{importsWithArtist.matchFoundPercentage}** of artist names found for imports");
            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{importsWithArtist.userPlays.Count(c => !string.IsNullOrWhiteSpace(c.ArtistName))}** with artist names");

            var plays = ImportService.AppleMusicImportsToValidUserPlays(contextUser, importsWithArtist.userPlays);
            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{plays.Count.Format(numberFormat)}** actual plays found");

            var playsWithoutDuplicates =
                await importService.RemoveDuplicateImports(contextUser.UserId, plays);
            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- **{playsWithoutDuplicates.Count.Format(numberFormat)}** new plays found");

            if (playsWithoutDuplicates.Count > 0)
            {
                await importService.InsertImportPlays(contextUser, playsWithoutDuplicates);
                await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"- Added plays to database");

                if (contextUser.DataSource == DataSource.LastFm)
                {
                    var userHasImportedLastfm = await playService.UserHasImportedLastFm(contextUser.UserId);

                    if (userHasImportedLastfm)
                    {
                        await userService.SetDataSource(contextUser, DataSource.FullImportThenLastFm);
                    }
                    else
                    {
                        await userService.SetDataSource(contextUser, DataSource.ImportThenFullLastFm);
                    }

                    await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                        $"- Updated import setting");
                }
            }

            if (contextUser.DataSource != DataSource.LastFm)
            {
                await indexService.RecalculateTopLists(contextUser);
                await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                    $"- Refreshed top list cache");
            }

            await importService.UpdateExistingScrobbleSource(contextUser);

            var years = await importBuilders.GetImportedYears(contextUser.UserId, PlaySource.AppleMusicImport,
                numberFormat);
            if (years is { Length: > 0 })
            {
                embed.AddField("<:fmbot_importing:1131511469096312914> All imported Apple Music plays", years, true);
            }

            contextUser = await userService.GetUserSettingsAsync(this.Context.User);

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
                embed.AddField("✅ Importing service activated", importActivated.ToString());
            }

            if (importSetting.Length > 0)
            {
                embed.AddField("⚙️ Current import setting", importSetting.ToString());
            }

            embed.WithColor(DiscordConstants.AppleMusicRed);

            var components = new ActionRowProperties()
                .WithButton("View your stats", $"{InteractionConstants.RecapAlltime}:{contextUser.UserId}",
                    style: ButtonStyle.Primary)
                .WithButton("Manage import settings", InteractionConstants.ImportManage, style: ButtonStyle.Secondary);

            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description, $"- Import complete!", true,
                components);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await UpdateAppleMusicImportEmbed(message.Id, this.Context.Interaction, embed, description,
                $"- ❌ Sorry, an internal error occured. Please try again later, or open a help thread on [our server](https://discord.gg/fmbot).",
                true);
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    private static async Task UpdateSpotifyImportEmbed(ulong messageId, Interaction interaction, EmbedProperties embed,
        StringBuilder builder, string lineToAdd, bool lastLine = false, ActionRowProperties components = null, string image = null)
    {
        await UpdateImportEmbed(messageId, interaction, embed, builder, lineToAdd, lastLine, components, image,
            PlaySource.SpotifyImport);
    }

    private static async Task UpdateAppleMusicImportEmbed(ulong messageId, Interaction interaction, EmbedProperties embed,
        StringBuilder builder, string lineToAdd, bool lastLine = false, ActionRowProperties components = null, string image = null)
    {
        await UpdateImportEmbed(messageId, interaction, embed, builder, lineToAdd, lastLine, components, image,
            PlaySource.AppleMusicImport);
    }

    private static async Task UpdateImportEmbed(ulong messageId, Interaction interaction, EmbedProperties embed,
        StringBuilder builder, string lineToAdd, bool lastLine = false, ActionRowProperties components = null, string image = null,
        PlaySource playSource = PlaySource.SpotifyImport)
    {
        builder.AppendLine(lineToAdd);

        var loadingLine =
            $"- {EmojiProperties.Custom(DiscordConstants.Loading).ToDiscordString("loading", true)} Processing...";

        var title = playSource == PlaySource.SpotifyImport
            ? $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} Importing history into .fmbot.."
            : $"{EmojiProperties.Custom(DiscordConstants.AppleMusic).ToDiscordString("apple_music")} Importing history into .fmbot..";

        var fieldsList = embed.Fields.ToList();
        var index = fieldsList.FindIndex(f => f.Name == title);
        fieldsList[index] = new EmbedFieldProperties()
            .WithName(title)
            .WithValue(builder + (lastLine ? null : loadingLine))
            .WithInline(true);
        embed.WithFields(fieldsList);

        if (image != null)
        {
            embed.WithImage(image);
        }

        await interaction.ModifyFollowupMessageAsync(messageId, m =>
        {
            m.Embeds = [embed];
            m.Components = components != null ? [components] : [];
        });
    }

    [SubSlashCommand("manage", "⭐ Manage your imports and configure how they are used")]
    [UsernameSetRequired]
    public async Task ManageImportAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response =
                await userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SubSlashCommand("modify", "⭐ Edit and delete artists, albums and tracks in your .fmbot imports")]
    [UsernameSetRequired]
    public async Task ModifyImportAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired);
            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedProperties()
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithDescription("Check your DMs to continue with modifying your .fmbot imports.");

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithEmbeds([serverEmbed])
                .WithFlags(MessageFlags.Ephemeral));
        }
        else if (this.Context.Channel != null)
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        }

        try
        {
            var response =
                await importBuilders.ImportModify(new ContextModel(this.Context, contextUser),
                    contextUser.UserId);
            var dmChannel = await this.Context.User.GetDMChannelAsync();
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
