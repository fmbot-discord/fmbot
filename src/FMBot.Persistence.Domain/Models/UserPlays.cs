using System;

namespace FMBot.Persistence.Domain.Models;

public class UserPlay
{
    public long UserPlayId { get; set; }

    public int UserId { get; set; }

    public string TrackName { get; set; }

    public string AlbumName { get; set; }

    public string ArtistName { get; set; }

    public DateTime TimePlayed { get; set; }

    public User User { get; set; }
}
