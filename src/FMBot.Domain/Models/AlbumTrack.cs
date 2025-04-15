namespace FMBot.Domain.Models;

public class AlbumTrack
{
    public string ArtistName { get; set; }

    public string TrackName { get; set; }
    public string TrackUrl { get; set; }

    public long? DurationSeconds { get; set; }

    public long? Rank { get; set; }

    public int? Playcount { get; set; }
}
