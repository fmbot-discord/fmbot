using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmArtwork
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("bgColor")]
    public string BgColor { get; set; }

    [JsonPropertyName("textColor1")]
    public string TextColor1 { get; set; }

    [JsonPropertyName("textColor2")]
    public string TextColor2 { get; set; }

    [JsonPropertyName("textColor3")]
    public string TextColor3 { get; set; }

    [JsonPropertyName("textColor4")]
    public string TextColor4 { get; set; }
}
