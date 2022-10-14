using Newtonsoft.Json;

namespace FMBot.Bot.Models;

public partial class SongResult
{
    [JsonProperty("annotation_count")]
    public long AnnotationCount { get; set; }

    [JsonProperty("api_path")]
    public string ApiPath { get; set; }

    [JsonProperty("full_title")]
    public string FullTitle { get; set; }

    [JsonProperty("header_image_thumbnail_url")]
    public string HeaderImageThumbnailUrl { get; set; }

    [JsonProperty("header_image_url")]
    public string HeaderImageUrl { get; set; }

    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("lyrics_owner_id")]
    public long LyricsOwnerId { get; set; }

    [JsonProperty("lyrics_state")]
    public string LyricsState { get; set; }

    [JsonProperty("path")]
    public string Path { get; set; }

    [JsonProperty("pyongs_count")]
    public object PyongsCount { get; set; }

    [JsonProperty("song_art_image_thumbnail_url")]
    public string SongArtImageThumbnailUrl { get; set; }

    [JsonProperty("song_art_image_url")]
    public string SongArtImageUrl { get; set; }

    [JsonProperty("stats")]
    public Stats Stats { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("title_with_featured")]
    public string TitleWithFeatured { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("primary_artist")]
    public PrimaryArtist PrimaryArtist { get; set; }
}

public partial class PrimaryArtist
{
    [JsonProperty("api_path")]
    public string ApiPath { get; set; }

    [JsonProperty("header_image_url")]
    public string HeaderImageUrl { get; set; }

    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("image_url")]
    public string ImageUrl { get; set; }

    [JsonProperty("is_meme_verified")]
    public bool IsMemeVerified { get; set; }

    [JsonProperty("is_verified")]
    public bool IsVerified { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }
}

public partial class Stats
{
    [JsonProperty("unreviewed_annotations")]
    public long UnreviewedAnnotations { get; set; }

    [JsonProperty("hot")]
    public bool Hot { get; set; }
}
