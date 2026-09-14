using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.Bot.Services.ThirdParty;

public class DeezerList<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }
}

public class DeezerGenre
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class DeezerArtist
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("picture_xl")]
    public string PictureXl { get; set; }

    [JsonPropertyName("nb_album")]
    public int? NbAlbum { get; set; }

    [JsonPropertyName("nb_fan")]
    public int? NbFan { get; set; }
}

public class DeezerAlbum
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("upc")]
    public string Upc { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("cover_xl")]
    public string CoverXl { get; set; }

    [JsonPropertyName("md5_image")]
    public string Md5Image { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; }

    [JsonPropertyName("record_type")]
    public string RecordType { get; set; }

    [JsonPropertyName("nb_tracks")]
    public int? NbTracks { get; set; }

    [JsonPropertyName("fans")]
    public int? Fans { get; set; }

    [JsonPropertyName("explicit_lyrics")]
    public bool? ExplicitLyrics { get; set; }

    [JsonPropertyName("explicit_content_lyrics")]
    public int? ExplicitContentLyrics { get; set; }

    [JsonPropertyName("genres")]
    public DeezerList<DeezerGenre> Genres { get; set; }

    [JsonPropertyName("artist")]
    public DeezerArtist Artist { get; set; }
}

public class DeezerTrack
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("readable")]
    public bool? Readable { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("title_short")]
    public string TitleShort { get; set; }

    [JsonPropertyName("title_version")]
    public string TitleVersion { get; set; }

    [JsonPropertyName("isrc")]
    public string Isrc { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; }

    [JsonPropertyName("explicit_lyrics")]
    public bool? ExplicitLyrics { get; set; }

    [JsonPropertyName("explicit_content_lyrics")]
    public int? ExplicitContentLyrics { get; set; }

    [JsonPropertyName("preview")]
    public string Preview { get; set; }

    [JsonPropertyName("bpm")]
    public float? Bpm { get; set; }

    [JsonPropertyName("gain")]
    public float? Gain { get; set; }

    [JsonPropertyName("artist")]
    public DeezerArtist Artist { get; set; }

    [JsonPropertyName("album")]
    public DeezerAlbum Album { get; set; }

    [JsonPropertyName("contributors")]
    public List<DeezerArtist> Contributors { get; set; }
}
