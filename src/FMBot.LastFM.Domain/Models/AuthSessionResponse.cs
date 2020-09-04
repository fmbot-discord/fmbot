using Newtonsoft.Json;

namespace FMBot.LastFM.Domain.Models
{
    public class AuthSessionResponse
    {
        [JsonProperty("session")]
        public Session Session { get; set; }
    }

    public class Session
    {
        [JsonProperty("subscriber")]
        public int Subscriber { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }
    }
}
