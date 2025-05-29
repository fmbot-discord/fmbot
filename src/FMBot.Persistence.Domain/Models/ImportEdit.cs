namespace FMBot.Persistence.Domain.Models;

public class ImportEdit
{
    public bool Applied { get; set; }

    public string OldArtistName { get; set; }
    public string NewArtistName { get; set; }

    public string OldAlbumName { get; set; }
    public string NewAlbumName { get; set; }

    public string OldTrackName { get; set; }
    public string NewTrackName { get; set; }

    public int OldPlaycount { get; set; }
    public int NewPlaycount { get; set; }
}

public enum ImportEditAction
{
    Rename = 1,
    RenameConfirmed = 2,
    Delete = 10,
    DeleteConfirmed = 11,
}
