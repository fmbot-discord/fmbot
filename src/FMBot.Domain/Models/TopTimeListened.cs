using System;

namespace FMBot.Domain.Models;

public class TopTimeListened
{
    public long MsPlayed { get; set; }
    public int PlaysWithPlayTime { get; set; }
    public TimeSpan TotalTimeListened { get; set; }
}
