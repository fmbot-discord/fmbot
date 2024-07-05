using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmAlbum
{
    [JsonPropertyName("data")]
    public List<AmData<AmAlbumAttributes>> Data { get; set; }
}

public class AmAlbumAttributes
{
    [JsonPropertyName("copyright")]
    public string Copyright { get; set; }

    [JsonPropertyName("genreNames")]
    public List<string> GenreNames { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }

    [JsonPropertyName("upc")]
    public string Upc { get; set; }

    [JsonPropertyName("isMasteredForItunes")]
    public bool IsMasteredForItunes { get; set; }

    [JsonPropertyName("artwork")]
    public AmArtwork Artwork { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("playParams")]
    public AmPlayParams PlayParams { get; set; }

    [JsonPropertyName("recordLabel")]
    public string RecordLabel { get; set; }

    [JsonPropertyName("isCompilation")]
    public bool IsCompilation { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("isSingle")]
    public bool IsSingle { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; }

    [JsonPropertyName("editorialNotes")]
    public AmEditorialNotes EditorialNotes { get; set; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("contentRating")]
    public string ContentRating { get; set; }
}
