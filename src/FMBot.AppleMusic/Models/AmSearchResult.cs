using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmSearchResult
{
    [JsonPropertyName("results")]
    public Results Results { get; set; }
}

public class Results
{
    [JsonPropertyName("songs")]
    public SongResult Songs { get; set; }
    
    [JsonPropertyName("albums")]
    public AlbumResult Albums { get; set; }
    
    [JsonPropertyName("artists")]
    public ArtistResult Artists { get; set; }
}

public class SongResult
{
    [JsonPropertyName("data")]
    public List<AmData<AmSongAttributes>> Data { get; set; }
}

public class AlbumResult
{
    [JsonPropertyName("data")]
    public List<AmData<AmAlbumAttributes>> Data { get; set; }
}

public class ArtistResult
{
    [JsonPropertyName("data")]
    public List<AmData<AmArtistAttributes>> Data { get; set; }
}
