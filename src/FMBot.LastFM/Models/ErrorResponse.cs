using Newtonsoft.Json;

namespace FMBot.LastFM.Models
{
    public class ErrorResponse
    {
        [JsonIgnore]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public LastResponseStatus? Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
