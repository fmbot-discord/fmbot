using System.Text.Json.Serialization;
using FMBot.Domain.Enums;

namespace FMBot.LastFM.Models;

internal class ErrorResponseLfm
{
    [JsonIgnore]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public ResponseStatus? Error { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}
