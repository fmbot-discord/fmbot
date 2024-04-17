using System.Text.Json.Serialization;
using System;

namespace FMBot.Domain.Models;

public class DataSourceUser
{
    public long Playcount { get; set; }
    public long ArtistCount { get; set; }
    public long AlbumCount { get; set; }
    public long TrackCount { get; set; }

    public string Name { get; set; }

    public bool Subscriber { get; set; }

    public string Url { get; set; }

    public string Country { get; set; }

    public string Image { get; set; }

    public long RegisteredUnix { get; set; }
    public DateTime Registered { get; set; }
    
    public long LfmRegisteredUnix { get; set; }

    public string Type { get; set; }
}
