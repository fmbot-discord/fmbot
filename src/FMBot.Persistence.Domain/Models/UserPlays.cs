using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class UserPlay
{
    public long UserPlayId { get; set; }

    public int UserId { get; set; }

    //public int? TrackId { get; set; }

    public string TrackName { get; set; }

    public string AlbumName { get; set; }

    public string ArtistName { get; set; }

    public DateTime TimePlayed { get; set; }

    public long? MsPlayed { get; set; }

    public PlaySource? PlaySource { get; set; }

    public User User { get; set; }

    //public Track Track { get; set; }
}
