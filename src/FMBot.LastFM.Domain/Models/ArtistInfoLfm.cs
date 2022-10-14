namespace FMBot.LastFM.Domain.Models;

public class ArtistInfoLfmResponse
{
    public ArtistInfoLfm Artist { get; set; }
}

public class ArtistInfoLfm
{
    public string Name { get; set; }
    public string Mbid { get; set; }
    public string Url { get; set; }
    public long Streamable { get; set; }
    public long Ontour { get; set; }
    public Stats Stats { get; set; }
    public TagsLfm Tags { get; set; }
    public Bio Bio { get; set; }
}

public class Bio
{
    public string Published { get; set; }
    public string Summary { get; set; }
    public string Content { get; set; }
}

public class Stats
{
    public long? Listeners { get; set; }
    public long? Playcount { get; set; }
    public long? Userplaycount { get; set; }
}
