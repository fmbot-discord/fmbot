using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmSong
{
    [JsonPropertyName("data")]
    public List<AmData<AmSongAttributes>> Data { get; set; }
}

public class AmSongAttributes
{
    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; }

    [JsonPropertyName("genreNames")]
    public List<string> GenreNames { get; set; }

    [JsonPropertyName("trackNumber")]
    public int TrackNumber { get; set; }

    [JsonPropertyName("durationInMillis")]
    public int DurationInMillis { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }

    [JsonPropertyName("isrc")]
    public string Isrc { get; set; }

    [JsonPropertyName("artwork")]
    public AmArtwork Artwork { get; set; }

    [JsonPropertyName("composerName")]
    public string ComposerName { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("playParams")]
    public AmPlayParams PlayParams { get; set; }

    [JsonPropertyName("discNumber")]
    public int DiscNumber { get; set; }

    [JsonPropertyName("hasCredits")]
    public bool HasCredits { get; set; }

    [JsonPropertyName("hasLyrics")]
    public bool HasLyrics { get; set; }

    [JsonPropertyName("isAppleDigitalMaster")]
    public bool IsAppleDigitalMaster { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("previews")]
    public List<AmPreview> Previews { get; set; }

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; }

    [JsonPropertyName("editorialNotes")]
    public AmEditorialNotes EditorialNotes { get; set; }
}
