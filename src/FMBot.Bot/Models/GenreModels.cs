using System;

namespace FMBot.Bot.Models;

public class GenreStreakCandidate
{
    public string GenreName { get; set; }

    public int Playcount { get; set; }

    public bool Alive { get; set; }

    public DateTime StreakStarted { get; set; }
}
