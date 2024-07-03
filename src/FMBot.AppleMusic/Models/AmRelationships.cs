using System.Text.Json.Serialization;

namespace FMBot.AppleMusic.Models;

public class AmRelationships
{
    [JsonPropertyName("albums")]
    public AmRelationshipList Albums { get; set; }

    [JsonPropertyName("artists")]
    public AmRelationshipList Artists { get; set; }

    [JsonPropertyName("songs")]
    public AmRelationshipList Songs { get; set; }
}

public class AmRelationshipList
{
    [JsonPropertyName("href")]
    public string Href { get; set; }

    [JsonPropertyName("next")]
    public string Next { get; set; }

    [JsonPropertyName("data")]
    public List<AmSubData> Data { get; set; }
}
