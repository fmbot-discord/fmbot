using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class TopAlbumsLfmResponse
{
    public TopAlbumsLfm TopAlbums { get; set; }
}

internal class TopAlbumsLfm
{
    [JsonPropertyName("@attr")]
    public TopListAttrLfm Attr { get; set; }
    public List<TopAlbumLfm> Album { get; set; }
}

internal class TopAlbumLfm
{
    public long Playcount { get; set; }
    public Artist Artist { get; set; }
    public List<ImageLfm> Image { get; set; }
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
}

internal class TopListAttrLfm
{
    public long Page { get; set; }
    public long Total { get; set; }
    public string User { get; set; }
    public long PerPage { get; set; }
    public long TotalPages { get; set; }
}
