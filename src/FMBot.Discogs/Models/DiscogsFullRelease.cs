using System.Text.Json.Serialization;

namespace FMBot.Discogs.Models;

public class DiscogsArtist
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

public class DiscogsExtraArtist
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

public class Identifier
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class Image
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; }

    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }

    [JsonPropertyName("uri150")]
    public string Uri150 { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class DiscogsFullRelease
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("resource_url")]
    public string ResourceUrl { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; }

    [JsonPropertyName("artists")]
    public List<DiscogsArtist> Artists { get; set; }

    [JsonPropertyName("artists_sort")]
    public string ArtistsSort { get; set; }

    [JsonPropertyName("labels")]
    public List<Label> Labels { get; set; }

    [JsonPropertyName("series")]
    public List<object> Series { get; set; }

    [JsonPropertyName("companies")]
    public List<object> Companies { get; set; }

    [JsonPropertyName("formats")]
    public List<Format> Formats { get; set; }

    [JsonPropertyName("data_quality")]
    public string DataQuality { get; set; }

    [JsonPropertyName("format_quantity")]
    public int FormatQuantity { get; set; }

    [JsonPropertyName("date_added")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("date_changed")]
    public DateTime DateChanged { get; set; }

    [JsonPropertyName("num_for_sale")]
    public int NumForSale { get; set; }

    [JsonPropertyName("lowest_price")]
    public double? LowestPrice { get; set; }

    [JsonPropertyName("master_id")]
    public int MasterId { get; set; }

    [JsonPropertyName("master_url")]
    public string MasterUrl { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("released")]
    public string Released { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; }

    [JsonPropertyName("released_formatted")]
    public string ReleasedFormatted { get; set; }

    [JsonPropertyName("identifiers")]
    public List<Identifier> Identifiers { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; }

    [JsonPropertyName("styles")]
    public List<string> Styles { get; set; }

    [JsonPropertyName("tracklist")]
    public List<DiscogsTracklistTrack> Tracklist { get; set; }

    [JsonPropertyName("extraartists")]
    public List<object> Extraartists { get; set; }

    [JsonPropertyName("images")]
    public List<Image> Images { get; set; }

    [JsonPropertyName("thumb")]
    public string Thumb { get; set; }

    [JsonPropertyName("estimated_weight")]
    public int EstimatedWeight { get; set; }

    [JsonPropertyName("blocked_from_sale")]
    public bool BlockedFromSale { get; set; }
}

public class DiscogsTracklistTrack
{
    [JsonPropertyName("position")]
    public string Position { get; set; }

    [JsonPropertyName("type_")]
    public string Type { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("extraartists")]
    public List<DiscogsExtraArtist> Extraartists { get; set; }

    [JsonPropertyName("duration")]
    public string Duration { get; set; }
}
