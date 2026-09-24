using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class GuildArtist
{
    public string ArtistName { get; set; }

    public int? ArtistId { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }

    public List<int> ListenerUserIds { get; set; }
}

public class GuildAlbum
{
    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public int? AlbumId { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public class GuildTrack
{
    public string ArtistName { get; set; }

    public string TrackName { get; set; }

    public int? TrackId { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public class GuildGenre
{
    public string GenreName { get; set; }

    public long TotalPlaycount { get; set; }

    public long ListenerCount { get; set; }
}
