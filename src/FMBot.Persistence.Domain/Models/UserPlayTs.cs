using System;

namespace FMBot.Persistence.Domain.Models;

public class UserPlayTs
{
    public DateTime TimePlayed { get; set; }

    public int UserId { get; set; }

    public string TrackName { get; set; }
    public string AlbumName { get; set; }
    public string ArtistName { get; set; }
}
