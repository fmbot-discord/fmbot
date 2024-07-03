using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmPlayParams
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; }
}
