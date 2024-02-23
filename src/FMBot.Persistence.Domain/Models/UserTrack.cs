namespace FMBot.Persistence.Domain.Models;

public class UserTrack
{
    public long UserTrackId { get; set; }

    public int UserId { get; set; }

    //public int? TrackId { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public int Playcount { get; set; }

    public User User { get; set; }

    //public Track Track { get; set; }
}
