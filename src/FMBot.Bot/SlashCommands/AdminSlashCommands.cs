using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.SlashCommands;

public class AdminSlashCommands : InteractionModuleBase
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly AlbumService _albumService;
    private readonly ArtistsService _artistService;
    private readonly IDataSourceFactory _dataSourceFactory;

    public AdminSlashCommands(AdminService adminService, CensorService censorService, AlbumService albumService, ArtistsService artistService, IDataSourceFactory dataSourceFactory)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._artistService = artistService;
        this._dataSourceFactory = dataSourceFactory;
    }

    [ComponentInteraction(InteractionConstants.CensorTypes)]
    public async Task SetCensoredArtist(string censoredId, string[] inputs)
    {
        var embed = new EmbedBuilder();

        var id = int.Parse(censoredId);
        var censoredMusic = await this._censorService.GetForId(id);

        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        foreach (var option in Enum.GetNames(typeof(CensorType)))
        {
            if (Enum.TryParse(option, out CensorType flag))
            {
                if (inputs.Any(a => a == option))
                {
                    censoredMusic.CensorType |= flag;
                }
                else
                {
                    censoredMusic.CensorType &= ~flag;
                }
            }
        }

        censoredMusic = await this._censorService.SetCensorType(censoredMusic, censoredMusic.CensorType);

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
        await RespondAsync(embed: embed.Build());
    }

    [ComponentInteraction(InteractionConstants.GlobalWhoKnowsReport)]
    public async Task ReportGlobalWhoKnowsButton()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportGlobalWhoKnowsModal>(InteractionConstants.GlobalWhoKnowsReportModal);
    }

    [ModalInteraction(InteractionConstants.GlobalWhoKnowsReportModal)]
    public async Task ReportGlobalWhoKnowsButton(ReportGlobalWhoKnowsModal modal)
    {
        var positiveResponse = $"Thanks, your report for the user `{modal.UserNameLastFM}` has been received. \n" +
                               $"You will currently not be notified if your report is processed.";

        var existingBottedUser = await this._adminService.GetBottedUserAsync(modal.UserNameLastFM);
        if (existingBottedUser is { BanActive: true })
        {
            await RespondAsync(positiveResponse, ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var existingReport = await this._adminService.UserReportAlreadyExists(modal.UserNameLastFM);
        if (existingReport)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the user you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync(positiveResponse, ephemeral: true);

        var report =
            await this._adminService.CreateBottedUserReportAsync(this.Context.User.Id, modal.UserNameLastFM, modal.Note);
        await this._adminService.PostReport(report);
    }

    [ComponentInteraction("gwk-report-ban-*")]
    public async Task GwkReportBan(string reportId)
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<ReportGlobalWhoKnowsBanModal>($"gwk-report-ban-confirmed-{reportId}-{message.Id}");
    }

    [ComponentInteraction("gwk-report-deny-*")]
    public async Task GwkReportDeny(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync("Error, report was already processed.", ephemeral: true);
            return;
        }

        await this._adminService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync("Report response processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ModalInteraction("gwk-report-ban-confirmed-*-*")]
    public async Task GwkReportBanConfirmed(string reportId, string messageId, ReportGlobalWhoKnowsBanModal modal)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync("Error, report was already processed.", ephemeral: true);
            return;
        }

        await this._adminService.UpdateReport(report, ReportStatus.AcceptedWithComment, this.Context.User.Id);

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(report.UserNameLastFM);
        DateTimeOffset? age = null;
        if (userInfo != null && userInfo.Subscriber)
        {
            age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        }

        var result = await this._adminService.AddBottedUserAsync(report.UserNameLastFM, modal.Note, age?.DateTime);

        if (result)
        {
            await RespondAsync("Report response processed, thank you.", ephemeral: true);
        }
        else
        {
            await RespondAsync($"Something went wrong. Try checking with `.checkbotted {report.UserNameLastFM}`", ephemeral: true);
        }

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Banned by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction(InteractionConstants.ReportAlbum)]
    public async Task ReportAlbum()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportAlbumModal>(InteractionConstants.ReportAlbumModal);
    }

    [ModalInteraction(InteractionConstants.ReportAlbumModal)]
    public async Task ReportAlbumButton(ReportAlbumModal modal)
    {
        var existingCensor = await this._censorService.AlbumResult(modal.AlbumName, modal.ArtistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync($"Thanks for your report, but the album cover you reported has already been marked as NSFW or censored.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var album = await this._albumService.GetAlbumFromDatabase(modal.ArtistName, modal.AlbumName);

        if (album == null)
        {
            await RespondAsync($"The album you tried to report does not exist in the .fmbot database (`{modal.AlbumName}` by `{modal.ArtistName}`). Someone needs to run the `album` command on it first.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.AlbumReportAlreadyExists(modal.ArtistName, modal.AlbumName);
        if (alreadyExists)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the album you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync($"Thanks, your report for the album `{modal.AlbumName}` by `{modal.ArtistName}` has been received.\n" +
                           $"You will currently not be notified if your report is processed.", ephemeral: true);

        var report = await this._censorService.CreateAlbumReport(this.Context.User.Id, modal.AlbumName, modal.ArtistName, modal.Note, album);
        await this._censorService.PostReport(report);
    }

    [ComponentInteraction(InteractionConstants.ReportArtist)]
    public async Task ReportArtist()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportArtistModal>(InteractionConstants.ReportArtistModal);
    }

    [ModalInteraction(InteractionConstants.ReportArtistModal)]
    public async Task ReportArtistButton(ReportArtistModal modal)
    {
        var existingCensor = await this._censorService.ArtistResult(modal.ArtistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync($"Thanks for your report, but the artist image you reported has already been marked as NSFW or censored.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var artist = await this._artistService.GetArtistFromDatabase(modal.ArtistName);

        if (artist == null)
        {
            await RespondAsync($"The artist you tried to report does not exist in the .fmbot database (`{modal.ArtistName}`). Someone needs to run the `artist` command on them first.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.ArtistReportAlreadyExists(modal.ArtistName);
        if (alreadyExists)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the artist image you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync($"Thanks, your report for the artist `{modal.ArtistName}` has been received.\n" +
                           $"You will currently not be notified if your report is processed.", ephemeral: true);

        var report = await this._censorService.CreateArtistReport(this.Context.User.Id, modal.ArtistName, modal.Note, artist);
        await this._censorService.PostReport(report);
    }

    [ComponentInteraction("censor-report-mark-nsfw-*")]
    public async Task MarkReportNsfw(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        if (report.IsArtist)
        {
            var existing = await this._censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This artist already exists in the censor db, use `.checkartist` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddArtist(report.ArtistName, CensorType.ArtistImageNsfw);
        }
        else
        {
            var existing = await this._censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This album already exists in the censor db, use `.checkalbum` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverNsfw);
        }

        await this._censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync("Report response processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Marked NSFW by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("censor-report-mark-censored-*")]
    public async Task MarkReportCensored(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        if (report.IsArtist)
        {
            var existing = await this._censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This artist already exists in the censor db, use `.checkartist` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddArtist(report.ArtistName, CensorType.ArtistImageCensored);
        }
        else
        {
            var existing = await this._censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This album already exists in the censor db, use `.checkalbum` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverCensored);
        }

        await this._censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync("Report processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Marked Censored by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("censor-report-deny-*")]
    public async Task MarkReportDenied(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        await this._censorService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync("Report processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = components.Build());
    }
}
