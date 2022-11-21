using System.Text.Json.Serialization;

namespace FMBot.Discogs.Models;

public class DiscogsIdentity
{
    public int Id { get; set; }
    public string Username { get; set; }
    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }
    [JsonPropertyName("consumer_name")]
    public string ConsumerName { get; set; }
}
