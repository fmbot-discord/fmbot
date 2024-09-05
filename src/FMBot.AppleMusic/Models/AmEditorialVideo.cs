using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmEditorialVideo
{
    [JsonPropertyName("motionSquareVideo1x1")]
    public AmEditorialVideoItem MotionSquareVideo1x1 { get; set; }

    [JsonPropertyName("motionTallVideo3x4")]
    public AmEditorialVideoItem MotionTallVideo3x4 { get; set; }

    [JsonPropertyName("motionDetailTall")]
    public AmEditorialVideoItem MotionDetailTall { get; set; }

    [JsonPropertyName("motionDetailSquare")]
    public AmEditorialVideoItem MotionDetailSquare { get; set; }
}

