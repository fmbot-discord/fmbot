using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public record TopArtistList
{
    public long? TotalAmount { get; init; }

    public string UserUrl { get; init; }
    public string UserTopArtistsUrl { get; init; }

    public IReadOnlyList<TopArtist> TopArtists { get; init; }
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
