using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public record TopAlbumList
{
    public long? TotalAmount { get; init; }

    public string UserUrl { get; init; }
    public string UserTopAlbumsUrl { get; init; }

    public IReadOnlyList<TopAlbum> TopAlbums { get; init; }
}

public class TopAlbum
{
    public string AlbumName { get; set; }
    public string AlbumUrl { get; set; }
    public string AlbumCoverUrl { get; set; }

    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }

    public long? Rank { get; set; }

    public long? UserPlaycount { get; set; }

    public DateTime? FirstPlay { get; set; }

    public TopTimeListened TimeListened { get; set; }

    public DateTime? ReleaseDate { get; set; }
    public string ReleaseDatePrecision { get; set; }
    public string AlbumType { get; set; }

    public Guid? Mbid { get; set; }
}
