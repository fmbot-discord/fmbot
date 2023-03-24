using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class AdminSlashCommands : InteractionModuleBase
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly AlbumService _albumService;
    private readonly ArtistsService _artistService;

    public AdminSlashCommands(AdminService adminService, CensorService censorService, AlbumService albumService, ArtistsService artistService)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._artistService = artistService;
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
        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [ComponentInteraction(InteractionConstants.GlobalWhoKnowsReport)]
    public async Task ReportGlobalWhoKnowsButton()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportGlobalWhoKnowsModal>(InteractionConstants.GlobalWhoKnowsReportModal);
    }

    [ModalInteraction(InteractionConstants.GlobalWhoKnowsReportModal)]
    public async Task ReportGlobalWhoKnowsButton(ReportGlobalWhoKnowsModal modal)
    {
        await RespondAsync($"You reported `{modal.UserNameLastFM}` with note `{modal.Note}`", ephemeral: true);

        var existingBottedUser = await this._adminService.GetBottedUserAsync(modal.UserNameLastFM);
        if (existingBottedUser is { BanActive: true })
        {
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }





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
            await RespondAsync($"The album you tried to report does not exist in the .fmbot database (`{modal.AlbumName}` by `{modal.ArtistName}`)", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await RespondAsync($"You reported `{modal.AlbumName}` by `{modal.ArtistName}` with note {modal.Note}", ephemeral: true);

        var report = await this._censorService.CreateAlbumReport(this.Context.User.Id, modal.AlbumName, modal.ArtistName, album);
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
            await RespondAsync($"The artist you tried to report does not exist in the .fmbot database (`{modal.ArtistName}`)", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await RespondAsync($"You reported `{modal.ArtistName}` with note {modal.Note}", ephemeral: true);

        var report = await this._censorService.CreateArtistReport(this.Context.User.Id, modal.ArtistName, artist);

        await this._censorService.PostReport(report);
    }

    [ComponentInteraction("censor-report-mark-nsfw-*")]
    public async Task MarkReportNsfw(string reportId, string[] inputs)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.IsArtist)
        {
            await this._censorService.AddArtist(report.ArtistName, CensorType.ArtistImageNsfw);
        }
        else
        {
            await this._censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverNsfw);
        }

        await this._censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        var msg = await Context.Channel.GetMessageAsync(this.Context.Interaction.Id);

        if (msg is IUserMessage message)
        {
            var components =
                new ComponentBuilder().WithButton($"Marked NSFW by {this.Context.Interaction.User.Username}", disabled: true);
            await message.ModifyAsync(m => m.Components = components.Build());
        }
    }

}
