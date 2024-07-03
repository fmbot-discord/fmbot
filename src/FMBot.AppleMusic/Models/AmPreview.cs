using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmPreview
{
    [JsonPropertyName("url")]
    public string Url { get; set; }
}
