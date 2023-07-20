using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TopTimeListened
{
    public long MsPlayed { get; set; }
    public int PlaysWithPlayTime { get; set; }
    public TimeSpan TotalTimeListened { get; set; }
    public List<CountedTrack> CountedTracks { get; set; }
}

public class CountedTrack
{
    public string Name { get; set; }
    public int CountedPlays { get; set; }
}
