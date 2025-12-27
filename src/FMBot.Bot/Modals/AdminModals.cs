using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class AdminModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly AlbumService _albumService;
    private readonly ArtistsService _artistService;

    public AdminModals(
        AdminService adminService,
        CensorService censorService,
        AlbumService albumService,
        ArtistsService artistService)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._artistService = artistService;
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.GlobalWhoKnowsReportModal)]
    public async Task ReportGlobalWhoKnowsButton()
    {
        var userNameLastFm = this.Context.GetModalValue("username_lastfm");
        var note = this.Context.GetModalValue("note");

        var positiveResponse = $"Thanks, your report for the user `{userNameLastFm}` has been received. \n" +
                               $"You will currently not be notified if your report is processed.";

        var existingBottedUser = await this._adminService.GetBottedUserAsync(userNameLastFm);
        if (existingBottedUser is { BanActive: true })
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent(positiveResponse)
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var existingReport = await this._adminService.UserReportAlreadyExists(userNameLastFm);
        if (existingReport)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the user you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(positiveResponse)
            .WithFlags(MessageFlags.Ephemeral)));

        var report =
            await this._adminService.CreateBottedUserReportAsync(this.Context.User.Id, userNameLastFm, note);
        await this._adminService.PostReport(report);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportAlbumModal)]
    public async Task ReportAlbumButton()
    {
        var albumName = this.Context.GetModalValue("album_name");
        var artistName = this.Context.GetModalValue("artist_name");
        var note = this.Context.GetModalValue("note");

        var existingCensor = await this._censorService.AlbumResult(albumName, artistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks for your report, but the album cover you reported has already been marked as NSFW or censored.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var album = await this._albumService.GetAlbumFromDatabase(artistName, albumName);

        if (album == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent($"The album you tried to report does not exist in the .fmbot database (`{albumName}` by `{artistName}`). Someone needs to run the `album` command on it first.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.AlbumReportAlreadyExists(artistName, albumName);
        if (alreadyExists)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the album you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Thanks, your report for the album `{albumName}` by `{artistName}` has been received.\n" +
                         "You will currently not be notified if your report is processed.")
            .WithFlags(MessageFlags.Ephemeral)));

        var report = await this._censorService.CreateAlbumReport(this.Context.User.Id, albumName,
            artistName, note, album);
        await this._censorService.PostReport(report);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportArtistModal)]
    public async Task ReportArtistButton()
    {
        var artistName = this.Context.GetModalValue("artist_name");
        var note = this.Context.GetModalValue("note");

        var existingCensor = await this._censorService.ArtistResult(artistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks for your report, but the artist image you reported has already been marked as NSFW or censored.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var artist = await this._artistService.GetArtistFromDatabase(artistName);

        if (artist == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent($"The artist you tried to report does not exist in the .fmbot database (`{artistName}`). Someone needs to run the `artist` command on them first.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.ArtistReportAlreadyExists(artistName);
        if (alreadyExists)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Thanks, but there is already a pending report for the artist image you reported.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"Thanks, your report for the artist `{artistName}` has been received.\n" +
                         "You will currently not be notified if your report is processed.")
            .WithFlags(MessageFlags.Ephemeral)));

        var report =
            await this._censorService.CreateArtistReport(this.Context.User.Id, artistName, note, artist);
        await this._censorService.PostReport(report);
    }
}
