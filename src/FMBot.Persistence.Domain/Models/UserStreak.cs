using System;
using System.Collections.Generic;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class UserStreak
{
    public long UserStreakId { get; set; }

    public int UserId { get; set; }

    public User User { get; set; }

    public string TrackName { get; set; }
    public int? TrackPlaycount { get; set; }

    public string AlbumName { get; set; }
    public int? AlbumPlaycount { get; set; }

    public string ArtistName { get; set; }
    public int? ArtistPlaycount { get; set; }

    public List<UserGenreStreak> GenreStreaks { get; set; }

    public DateTime StreakStarted { get; set; }
    public DateTime StreakEnded { get; set; }
}
