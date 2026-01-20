using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class AdminInteractions(
    AdminService adminService,
    CensorService censorService,
    AlbumService albumService,
    ArtistsService artistService,
    IDataSourceFactory dataSourceFactory,
    AliasService aliasService,
    PlayService playService,
    FriendsService friendsService,
    UserService userService,
    SupporterService supporterService,
    GuildService guildService)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.ModerationCommands.CensorTypes)]
    public async Task SetCensoredArtist(string censoredId)
    {
        var embed = new EmbedProperties();

        var id = int.Parse(censoredId);
        var censoredMusic = await censorService.GetForId(id);

        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        foreach (var option in Enum.GetNames(typeof(CensorType)))
        {
            if (Enum.TryParse(option, out CensorType flag))
            {
                if (selectedValues.Any(a => a == option))
                {
                    censoredMusic.CensorType |= flag;
                }
                else
                {
                    censoredMusic.CensorType &= ~flag;
                }
            }
        }

        censoredMusic = await censorService.SetCensorType(censoredMusic, censoredMusic.CensorType);

        var description = new StringBuilder();

        if (censoredMusic.Artist)
        {
            description.AppendLine($"Artist: `{censoredMusic.ArtistName}`");
        }
        else
        {
            description.AppendLine($"Album: `{censoredMusic.AlbumName}` by `{censoredMusic.ArtistName}`");
        }

        description.AppendLine();
        description.AppendLine("Censored music entry has been updated to:");

        foreach (var flag in censoredMusic.CensorType.GetUniqueFlags())
        {
            if (censoredMusic.CensorType.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithFooter($"Adjusted by {this.Context.Interaction.User.Username}");
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])));
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ArtistAlias)]
    public async Task SetArtistAliasOptions(string censoredId)
    {
        var embed = new EmbedProperties();

        var id = int.Parse(censoredId);
        var artistAlias = await aliasService.GetArtistAliasForId(id);

        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        foreach (var option in Enum.GetNames(typeof(AliasOption)))
        {
            if (Enum.TryParse(option, out AliasOption flag))
            {
                if (selectedValues.Any(a => a == option))
                {
                    artistAlias.Options |= flag;
                }
                else
                {
                    artistAlias.Options &= ~flag;
                }
            }
        }

        artistAlias = await aliasService.SetAliasOptions(artistAlias, artistAlias.Options);

        var description = new StringBuilder();

        description.AppendLine($"Artist: `{artistAlias.Artist.Name}`");
        description.AppendLine($"Alias: `{artistAlias.Alias}`");

        description.AppendLine();
        description.AppendLine("Artist alias has been updated to:");

        foreach (var flag in artistAlias.Options.GetUniqueFlags())
        {
            if (artistAlias.Options.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        aliasService.RemoveCache();

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithFooter($"Adjusted by {this.Context.Interaction.User.Username}");
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])));
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.GlobalWhoKnowsReport)]
    public async Task ReportGlobalWhoKnowsOpenModal()
    {
        await RespondAsync(InteractionCallback.Modal(ModalFactory.CreateReportGlobalWhoKnowsModal(InteractionConstants
            .ModerationCommands.GlobalWhoKnowsReportModal)));
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.GlobalWhoKnowsReportModal)]
    public async Task ReportGlobalWhoKnowsButton()
    {
        var userNameLastFm = this.Context.GetModalValue("username_lastfm");
        var note = this.Context.GetModalValue("note");

        var positiveResponse = $"Thanks, your report for the user `{userNameLastFm}` has been received. \n" +
                               $"You will currently not be notified if your report is processed.";

        var existingBottedUser = await adminService.GetBottedUserAsync(userNameLastFm);
        if (existingBottedUser is { BanActive: true })
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent(positiveResponse)
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Censored }, userService);
            return;
        }

        var existingReport = await adminService.UserReportAlreadyExists(userNameLastFm);
        if (existingReport)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the user you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(positiveResponse)
            .WithFlags(MessageFlags.Ephemeral)));

        var report =
            await adminService.CreateBottedUserReportAsync(this.Context.User.Id, userNameLastFm, note);
        await adminService.PostReport(report);
    }

    [ComponentInteraction("gwk-report-ban")]
    public async Task GwkReportBan(string reportId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        await RespondAsync(InteractionCallback.Modal(ModalFactory.CreateReportGlobalWhoKnowsBanModal(
            $"gwk-report-ban-confirmed:{reportId}:{message?.Id ?? 0}")));
    }

    [ComponentInteraction("gwk-report-deny")]
    public async Task GwkReportDeny(string reportId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var id = int.Parse(reportId);
        var report = await adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Error, report was already processed.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await adminService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Report response processed, thank you.")
                .WithFlags(MessageFlags.Ephemeral)));

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1",
                disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction("gwk-report-ban-confirmed")]
    public async Task GwkReportBanConfirmed(string reportId, string messageId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var note = this.Context.GetModalValue("note");

        var id = int.Parse(reportId);
        var report = await adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Error, report was already processed.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await adminService.UpdateReport(report, ReportStatus.AcceptedWithComment, this.Context.User.Id);

        var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(report.UserNameLastFM);
        DateTimeOffset? age = null;
        if (userInfo != null && userInfo.Subscriber)
        {
            age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        }

        var result = await adminService.AddBottedUserAsync(report.UserNameLastFM, note, age?.DateTime);

        if (result)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Report response processed, thank you.")
                .WithFlags(MessageFlags.Ephemeral)));
        }
        else
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent($"Something went wrong. Try checking with `.checkbotted {report.UserNameLastFM}`")
                .WithFlags(MessageFlags.Ephemeral)));
        }

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        var components =
            new ActionRowProperties().WithButton($"Banned by {this.Context.Interaction.User.Username}", customId: "1",
                url: null, disabled: true, style: ButtonStyle.Success);
        await msg.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction("gwk-filtered-user-to-ban")]
    public async Task GwkFilteredUserToBan(string filteredUserId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var id = int.Parse(filteredUserId);
        var filteredUser = await adminService.GetFilteredUserForIdAsync(id);

        var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(filteredUser.UserNameLastFm);
        DateTimeOffset? age = null;
        if (userInfo != null && userInfo.Subscriber)
        {
            age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        }

        var filterInfo = WhoKnowsFilterService.FilteredUserReason(filteredUser);

        var result =
            await adminService.AddBottedUserAsync(filteredUser.UserNameLastFm, filterInfo.ToString(),
                age?.DateTime);

        if (result)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Converted filter for `{filteredUser.UserNameLastFm}` into gwk ban, thank you.")
            .WithFlags(MessageFlags.Ephemeral)));
        }
        else
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Something went wrong. Try checking again")
                .WithFlags(MessageFlags.Ephemeral)));
        }

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Converted to ban by {this.Context.Interaction.User.Username}",
                customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportAlbum)]
    public async Task ReportAlbumOpenModal()
    {
        await RespondAsync(InteractionCallback.Modal(
            new ModalProperties(InteractionConstants.ModerationCommands.ReportAlbumModal, "Report album")
                .WithComponents(ModalFactory.CreateReportAlbumModal(InteractionConstants.ModerationCommands.ReportAlbumModal))));
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportAlbumModal)]
    public async Task ReportAlbumButton()
    {
        var albumName = this.Context.GetModalValue("album_name");
        var artistName = this.Context.GetModalValue("artist_name");
        var note = this.Context.GetModalValue("note");

        var existingCensor = await censorService.AlbumResult(albumName, artistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks for your report, but the album cover you reported has already been marked as NSFW or censored.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Censored }, userService);
            return;
        }

        var album = await albumService.GetAlbumFromDatabase(artistName, albumName);

        if (album == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent($"The album you tried to report does not exist in the .fmbot database (`{albumName}` by `{artistName}`). Someone needs to run the `album` command on it first.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var alreadyExists = await censorService.AlbumReportAlreadyExists(artistName, albumName);
        if (alreadyExists)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the album you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Thanks, your report for the album `{albumName}` by `{artistName}` has been received.\n" +
                         "You will currently not be notified if your report is processed.")
            .WithFlags(MessageFlags.Ephemeral)));

        var report = await censorService.CreateAlbumReport(this.Context.User.Id, albumName,
            artistName, note, album);
        await censorService.PostReport(report);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportArtist)]
    public async Task ReportArtistOpenModal()
    {
        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateReportArtistModal(InteractionConstants.ModerationCommands.ReportArtistModal)));
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportArtistModal)]
    public async Task ReportArtistButton()
    {
        var artistName = this.Context.GetModalValue("artist_name");
        var note = this.Context.GetModalValue("note");

        var existingCensor = await censorService.ArtistResult(artistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks for your report, but the artist image you reported has already been marked as NSFW or censored.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Censored }, userService);
            return;
        }

        var artist = await artistService.GetArtistFromDatabase(artistName);

        if (artist == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent($"The artist you tried to report does not exist in the .fmbot database (`{artistName}`). Someone needs to run the `artist` command on them first.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var alreadyExists = await censorService.ArtistReportAlreadyExists(artistName);
        if (alreadyExists)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the artist image you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Thanks, your report for the artist `{artistName}` has been received.\n" +
                         "You will currently not be notified if your report is processed.")
            .WithFlags(MessageFlags.Ephemeral)));

        var report =
            await censorService.CreateArtistReport(this.Context.User.Id, artistName, note, artist);
        await censorService.PostReport(report);
    }

    [ComponentInteraction("censor-report-mark-nsfw")]
    public async Task MarkReportNsfw(string reportId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var id = int.Parse(reportId);
        var report = await censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This report has already been processed.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (report.IsArtist)
        {
            var existing = await censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This artist already exists in the censor db, use `.checkartist` instead!")
                .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await censorService.AddArtist(report.ArtistName, CensorType.ArtistImageNsfw);
        }
        else
        {
            var existing = await censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This album already exists in the censor db, use `.checkalbum` instead!")
                .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverNsfw);
        }

        await censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Report response processed, thank you.")
                .WithFlags(MessageFlags.Ephemeral)));

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Marked NSFW by {this.Context.Interaction.User.Username}", customId: "1",
                url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction("censor-report-mark-censored")]
    public async Task MarkReportCensored(string reportId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var id = int.Parse(reportId);
        var report = await censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This report has already been processed.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (report.IsArtist)
        {
            var existing = await censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This artist already exists in the censor db, use `.checkartist` instead!")
                .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await censorService.AddArtist(report.ArtistName, CensorType.ArtistImageCensored);
        }
        else
        {
            var existing = await censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This album already exists in the censor db, use `.checkalbum` instead!")
                .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverCensored);
        }

        await censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Report processed, thank you.")
                .WithFlags(MessageFlags.Ephemeral)));

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Marked Censored by {this.Context.Interaction.User.Username}",
                customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction("censor-report-deny")]
    public async Task MarkReportDenied(string reportId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var id = int.Parse(reportId);
        var report = await censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This report has already been processed.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await censorService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Report processed, thank you.")
                .WithFlags(MessageFlags.Ephemeral)));

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1",
                url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.MoveUserData)]
    public async Task MoveUserData(string oldUserId, string newUserId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Moving data...")
                .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.DisableInteractionButtons();

        try
        {
            await playService.MoveData(int.Parse(oldUserId), int.Parse(newUserId));

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithContent("Moving data completed.")
                .WithFlags(MessageFlags.Ephemeral));

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var components =
                new ActionRowProperties().WithButton($"Moved by {this.Context.Interaction.User.Username}", customId: "1",
                    url: null, disabled: true, style: ButtonStyle.Danger);
            await message.ModifyAsync(m => m.Components = [components]);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.MoveSupporter)]
    public async Task MoveSupporter(string oldUserId, string newUserId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Moving supporter...")
                .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.DisableInteractionButtons();

        try
        {
            await supporterService.MigrateDiscordForSupporter(ulong.Parse(oldUserId), ulong.Parse(newUserId));

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithContent("Moving supporter completed.")
                .WithFlags(MessageFlags.Ephemeral));

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var components =
                new ActionRowProperties().WithButton($"Moved by {this.Context.Interaction.User.Username}", customId: "1",
                    url: null, disabled: true, style: ButtonStyle.Danger);
            await message.ModifyAsync(m => m.Components = [components]);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction("admin-delete-user")]
    public async Task DeleteUser(string userId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You don't have permission to do this.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Deleting user...")
                .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.DisableInteractionButtons();

        await friendsService.RemoveAllFriendsAsync(int.Parse(userId));
        await friendsService.RemoveUserFromOtherFriendsAsync(int.Parse(userId));

        await userService.DeleteUser(int.Parse(userId));

        await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithContent("Deleting user completed.")
            .WithFlags(MessageFlags.Ephemeral));

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Deleted by {this.Context.Interaction.User.Username}", customId: "1",
                url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = [components]);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.GuildFlags)]
    public async Task SetGuildFlags(string discordGuildId)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent(Constants.FmbotStaffOnly)
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var discordId = ulong.Parse(discordGuildId);
        var guild = await guildService.GetGuildAsync(discordId);
        if (guild == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Guild not found in database")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        var newFlags = (GuildFlags)0;
        foreach (var option in Enum.GetNames<GuildFlags>())
        {
            if (Enum.TryParse(option, out GuildFlags flag))
            {
                if (selectedValues.Any(a => a == option))
                {
                    newFlags |= flag;
                }
                else
                {
                    newFlags &= ~flag;
                }
            }
        }

        await guildService.SetGuildFlags(guild.GuildId, newFlags);

        var flagsDescription = newFlags == 0 ? "None" : string.Join(", ", selectedValues);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Guild flags updated to: {flagsDescription}")
            .WithFlags(MessageFlags.Ephemeral)));
    }
}
