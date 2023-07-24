using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class AlbumAutoCompleteSearchModel
{
    public AlbumAutoCompleteSearchModel(
        string artist,
        string album,
        int? popularity = null)
    {
        this.Artist = artist;
        this.Album = album;
        this.Name = $"{artist} | {album}";
    }

    public AlbumAutoCompleteSearchModel(string name)
    {
        this.Name = name;
    }

    public string Name { get; set;  }

    public string Album { get; }

    public string Artist { get; }

    public int? Popularity { get; }
}

public class GuildAlbum
{
    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public class WhoKnowsAlbumDto
{
    public int UserId { get; set; }

    public string UserNameLastFm { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public int Playcount { get; set; }
}

public class AlbumCoverDto
{
    public string LastfmImageUrl { get; set; }

    public string SpotifyImageUrl { get; set; }

    public string AlbumName { get; set; }
    public string ArtistName { get; set; }
}

public class WhoKnowsGlobalAlbumDto
{
    public int UserId { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public int Playcount { get; set; }

    public string UserNameLastFm { get; set; }

    public ulong DiscordUserId { get; set; }

    public DateTime? RegisteredLastFm { get; set; }

    public PrivacyLevel PrivacyLevel { get; set; }
}

public class AlbumSearch
{
    public AlbumSearch(AlbumInfo album, ResponseModel response, int? randomAlbumPosition = null, long? randomAlbumPlaycount = null)
    {
        this.Album = album;
        this.Response = response;
        this.IsRandom = randomAlbumPosition.HasValue && randomAlbumPlaycount.HasValue;
        this.RandomAlbumPosition = randomAlbumPosition + 1;
        this.RandomAlbumPlaycount = randomAlbumPlaycount;
    }

    public AlbumInfo Album { get; set; }
    public ResponseModel Response { get; set; }

    public bool IsRandom { get; set; }
    public int? RandomAlbumPosition { get; set; }
    public long? RandomAlbumPlaycount { get; set; }
}
