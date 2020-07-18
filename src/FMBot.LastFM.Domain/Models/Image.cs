using Newtonsoft.Json;

namespace FMBot.LastFM.Domain.Models
{
    public class Image
    {
        [JsonProperty("#text")]
        public string Text { get; set; }
        public string Size { get; set; }
    }
}
