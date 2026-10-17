namespace FMBot.Images.Models;

public class RecapCard
{
    public string Title { get; init; }
    public string PhotoUrl { get; init; }

    public string TopArtistLabel { get; init; }
    public string TopArtistName { get; init; }
    public string TopArtistLine { get; init; }

    public IReadOnlyList<RecapCardStat> Stats { get; init; }

    public string TopArtistsLabel { get; init; }
    public IReadOnlyList<RecapCardItem> Artists { get; init; }

    public string TopTracksLabel { get; init; }
    public IReadOnlyList<RecapCardItem> Tracks { get; init; }

    public IReadOnlyList<string> Genres { get; init; }
}

public record RecapCardStat(string Number, string Label);

public record RecapCardItem(string Name, string Subtitle, string ImageUrl);
