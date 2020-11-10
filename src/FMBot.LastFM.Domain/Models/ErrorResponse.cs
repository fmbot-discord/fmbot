using System.Text.Json.Serialization;
using FMBot.LastFM.Domain.Enums;

namespace FMBot.LastFM.Domain.Models
{
    public class ErrorResponse
    {
        [JsonIgnore]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public ResponseStatus? Error { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
