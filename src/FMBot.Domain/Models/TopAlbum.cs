using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TopAlbumList
{
    public long? TotalAmount { get; set; }

    public string UserUrl { get; set; }
    public string UserTopAlbumsUrl { get; set; }

    public List<TopAlbum> TopAlbums { get; set; }
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

    public Guid? Mbid { get; set; }
}
