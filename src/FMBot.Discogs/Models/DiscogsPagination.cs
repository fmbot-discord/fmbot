using System.Text.Json.Serialization;

namespace FMBot.Discogs.Models;

public class DiscogsPagination
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    [JsonPropertyName("items")]
    public int Items { get; set; }

    [JsonPropertyName("urls")]
    public DiscogsPaginationUrls Urls { get; set; }
}

public class DiscogsPaginationUrls
{
    [JsonPropertyName("last")]
    public string Last { get; set; }

    [JsonPropertyName("next")]
    public string Next { get; set; }
}
