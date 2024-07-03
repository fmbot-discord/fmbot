using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmEditorialNotes
{
    [JsonPropertyName("tagline")]
    public string Tagline { get; set; }

    [JsonPropertyName("standard")]
    public string Standard { get; set; }

    [JsonPropertyName("short")]
    public string Short { get; set; }
}
