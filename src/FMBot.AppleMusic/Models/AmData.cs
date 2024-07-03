using System.Text.Json.Serialization;
using FMBot.AppleMusic.Enums;

namespace FMBot.AppleMusic.Models;

public class AmData<T>
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public AmType? Type { get; set; }

    [JsonPropertyName("href")]
    public string Href { get; set; }

    [JsonPropertyName("attributes")]
    public T Attributes { get; set; }

    [JsonPropertyName("relationships")]
    public AmRelationships Relationships { get; set; }
}

public class AmSubData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public AmType Type { get; set; }

    [JsonPropertyName("href")]
    public string Href { get; set; }
}
