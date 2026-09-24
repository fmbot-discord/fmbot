namespace FMBot.Domain.Models;

public class WhoKnowsArtistDto
{
    public int UserId { get; set; }

    public string UserNameLastFm { get; set; }

    public int Playcount { get; set; }
}

public class WhoKnowsAlbumDto
{
    public int UserId { get; set; }

    public string UserNameLastFm { get; set; }

    public int Playcount { get; set; }
}

public class WhoKnowsTrackDto
{
    public int UserId { get; set; }

    public string UserNameLastFm { get; set; }

    public int Playcount { get; set; }
}
