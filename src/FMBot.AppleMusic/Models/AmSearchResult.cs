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
    public ResultItem Songs { get; set; }
}

public class ResultItem
{
    [JsonPropertyName("data")]
    public List<AmData<AmSongAttributes>> Data { get; set; }
}
