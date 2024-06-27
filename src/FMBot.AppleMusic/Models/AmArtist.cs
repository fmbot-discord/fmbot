using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmArtist
{
    [JsonPropertyName("data")]
    public List<AmData<AmArtistAttributes>> Data { get; set; }
}

public class AmArtistAttributes
{
    [JsonPropertyName("genreNames")]
    public List<string> GenreNames { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("artwork")]
    public AmArtwork Artwork { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}
