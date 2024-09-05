using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmEditorialVideoItem
{
    [JsonPropertyName("previewFrame")]
    public AmArtwork PreviewFrame { get; set; }

    [JsonPropertyName("video")]
    public string Video { get; set; }
}

