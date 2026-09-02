using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class TopArtistsLfmResponse
{
    public TopArtistsLfm TopArtists { get; set; }
}

internal class TopArtistsLfm
{
    [JsonPropertyName("@attr")]
    public TopListAttrLfm Attr { get; set; }
    public List<TopArtistLfm> Artist { get; set; }
}

internal class TopArtistLfm
{
    public long Playcount { get; set; }
    public List<ImageLfm> Image { get; set; }
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
}
