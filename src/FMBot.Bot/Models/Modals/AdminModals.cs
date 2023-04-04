using Discord;
using Discord.Interactions;

namespace FMBot.Bot.Models.Modals;


public class ReportGlobalWhoKnowsModal : IModal
{
    public string Title => "Report GlobalWhoKnows user";

    [InputLabel("Enter Last.fm username")]
    [ModalTextInput("lastfm_username", placeholder: "fm-bot", minLength: 2, maxLength: 15)]
    public string UserNameLastFM { get; set; }

    [InputLabel("Add note (optional)")]
    [ModalTextInput("note", placeholder: "8 days listening time in a week", maxLength: 300, style: TextInputStyle.Paragraph)]
    [RequiredInput(false)]
    public string Note { get; set; }
}

public class ReportGlobalWhoKnowsBanModal : IModal
{
    public string Title => "Confirm GlobalWhoKnows ban";

    [InputLabel("Add admin note")]
    [ModalTextInput("note", placeholder: "8 days listening time in a week", maxLength: 300, style: TextInputStyle.Paragraph)]
    public string Note { get; set; }
}

public class ReportArtistModal : IModal
{
    public string Title => "Report artist";

    [InputLabel("Artist")]
    [ModalTextInput("artist_name", placeholder: "Death Grips", minLength: 2, maxLength: 150)]
    public string ArtistName { get; set; }

    [InputLabel("Add note (optional)")]
    [ModalTextInput("note", maxLength: 300, style: TextInputStyle.Paragraph)]
    [RequiredInput(false)]
    public string Note { get; set; }
}

public class ReportAlbumModal : IModal
{
    public string Title => "Report album";

    [InputLabel("Artist")]
    [ModalTextInput("artist_name", placeholder: "Death Grips", minLength: 2, maxLength: 150)]
    public string ArtistName { get; set; }

    [InputLabel("Album")]
    [ModalTextInput("album_name", placeholder: "No Love Deep Web", minLength: 2, maxLength: 150)]
    public string AlbumName { get; set; }

    [InputLabel("Add note (optional)")]
    [ModalTextInput("note", maxLength: 300, style: TextInputStyle.Paragraph)]
    [RequiredInput(false)]
    public string Note { get; set; }
}


public class DenyReportModal : IModal
{
    public string Title => "Deny report";

    [InputLabel("Add note to send to user (optional)")]
    [ModalTextInput("note", maxLength: 300, style: TextInputStyle.Paragraph)]
    [RequiredInput(false)]
    public string Note { get; set; }
}
