using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class ArtistInfo
{
    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }

    public long TotalListeners { get; set; }
    public long TotalPlaycount { get; set; }
    public long? UserPlaycount { get; set; }

    public string Description { get; set; }

    public List<Tag> Tags { get; set; }

    public Guid? Mbid { get; set; }
}
