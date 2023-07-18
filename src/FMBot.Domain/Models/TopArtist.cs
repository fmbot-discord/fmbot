using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TopArtistList
{
    public long? TotalAmount { get; set; }

    public string UserUrl { get; set; }
    public string UserTopArtistsUrl { get; set; }

    public List<TopArtist> TopArtists { get; set; }
}

public class TopArtist
{
    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }
    public string ArtistImageUrl { get; set; }

    public long? Rank { get; set; }

    public long UserPlaycount { get; set; }

    public Guid? Mbid { get; set; }

    public DateTime? FirstPlay { get; set; }

    public TopTimeListened TimeListened { get; set; }

    public List<string> Genres { get; set; }
}

public class TopDiscogsArtist
{
    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }
    public long? UserReleasesInCollection { get; set; }
    public DateTime? FirstAdded { get; set; }
}
