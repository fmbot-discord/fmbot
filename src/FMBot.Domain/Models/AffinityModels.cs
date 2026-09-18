namespace FMBot.Domain.Models;

public class AffinityItemDto
{
    public int UserId { get; set; }

    public string Name { get; set; }
    public long Playcount { get; set; }
    public int Position { get; set; }
}

public class AffinityUser
{
    public int UserId { get; set; }

    public double GenrePoints { get; set; }

    public double ArtistPoints { get; set; }

    public double CountryPoints { get; set; }

    public double TotalPoints { get; set; }
}
