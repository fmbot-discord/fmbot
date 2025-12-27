

namespace FMBot.Bot.Models.Modals;

public class ReportGlobalWhoKnowsModal
{
    public string UserNameLastFM { get; set; }
    public string Note { get; set; }
}

public class ReportGlobalWhoKnowsBanModal
{
    public string Note { get; set; }
}

public class ReportArtistModal
{
    public string ArtistName { get; set; }
    public string Note { get; set; }
}

public class ReportAlbumModal
{
    public string ArtistName { get; set; }
    public string AlbumName { get; set; }
    public string Note { get; set; }
}

public class DenyReportModal
{
    public string Note { get; set; }
}
