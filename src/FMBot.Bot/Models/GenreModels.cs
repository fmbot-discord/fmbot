using System;

namespace FMBot.Bot.Models;

public class GenreStreakCandidate
{
    public string GenreName { get; set; }

    public int Playcount { get; set; }

    public bool Alive { get; set; }

    public DateTime StreakStarted { get; set; }
}

public class ArtistGenreDto
{
    public string Genre { get; set; }

    public string ArtistName { get; set; }
}

public class GuildGenre
{
    public string GenreName { get; set; }

    public long TotalPlaycount { get; set; }

    public long ListenerCount { get; set; }
}
