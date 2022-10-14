using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models;

public class ImageLfm
{
    [JsonPropertyName("#text")]
    public string Text { get; set; }
    public string Size { get; set; }
}
