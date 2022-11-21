using System.Text.Json.Serialization;

namespace FMBot.Discogs.Models;

public class DiscogsUserReleases
{
    [JsonPropertyName("pagination")]
    public DiscogsPagination Pagination { get; set; }

    [JsonPropertyName("releases")]
    public List<Release> Releases { get; set; }
}

public class Artist
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("anv")]
    public string Anv { get; set; }

    [JsonPropertyName("join")]
    public string Join { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("tracks")]
    public string Tracks { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }
}

public class BasicInformation
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("master_id")]
    public int MasterId { get; set; }

    [JsonPropertyName("master_url")]
    public string MasterUrl { get; set; }

    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }

    [JsonPropertyName("thumb")]
    public string Thumb { get; set; }

    [JsonPropertyName("cover_image")]
    public string CoverImage { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("formats")]
    public List<Format> Formats { get; set; }

    [JsonPropertyName("labels")]
    public List<Label> Labels { get; set; }

    [JsonPropertyName("artists")]
    public List<Artist> Artists { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; }

    [JsonPropertyName("styles")]
    public List<string> Styles { get; set; }
}

public class Format
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("qty")]
    public string Qty { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("descriptions")]
    public List<string> Descriptions { get; set; }
}

public class Label
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("catno")]
    public string Catno { get; set; }

    [JsonPropertyName("entity_type")]
    public string EntityType { get; set; }

    [JsonPropertyName("entity_type_name")]
    public string EntityTypeName { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }
}

public class Release
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("instance_id")]
    public int InstanceId { get; set; }

    [JsonPropertyName("date_added")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("rating")]
    public int Rating { get; set; }

    [JsonPropertyName("basic_information")]
    public BasicInformation BasicInformation { get; set; }
}
