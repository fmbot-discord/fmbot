using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class GuildTrack
{
    public string ArtistName { get; set; }

    public string TrackName { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public class WhoKnowsTrackDto
{
    public int UserId { get; set; }

    public string UserNameLastFm { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public int Playcount { get; set; }
}

public class WhoKnowsGlobalTrackDto
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

public class AlbumTrackDto
{
    public string TrackName { get; set; }

    public string ArtistName { get; set; }

    public long DurationMs { get; set; }
}

public class TrackAutoCompleteSearchModel
{
    public TrackAutoCompleteSearchModel(
        string artist,
        string track,
        int? popularity = null)
    {
        this.Artist = artist;
        this.Track = track;
        this.Name = $"{artist} | {track}";
    }

    public TrackAutoCompleteSearchModel(string name)
    {
        this.Name = name;
    }

    public string Name { get; set; }

    public string Track { get; }

    public string Artist { get; }

    public int? Popularity { get; }
}


public class TrackLengthDto
{
    public string ArtistName { get; set; }
    public string TrackName { get; set; }

    public long DurationMs { get; set; }
}

public class TrackSearchResult
{
    public string TrackName { get; set; }
    public string ArtistName { get; set; }
    public string AlbumName { get; set; }
}


public class TrackSearch
{
    public TrackSearch(TrackInfo track, ResponseModel response, int? randomAlbumPosition = null, long? randomAlbumPlaycount = null)
    {
        this.Track = track;
        this.Response = response;
        this.IsRandom = randomAlbumPosition.HasValue && randomAlbumPlaycount.HasValue;
        this.RandomAlbumPosition = randomAlbumPosition + 1;
        this.RandomAlbumPlaycount = randomAlbumPlaycount;
    }

    public TrackInfo Track { get; set; }
    public ResponseModel Response { get; set; }

    public bool IsRandom { get; set; }
    public int? RandomAlbumPosition { get; set; }
    public long? RandomAlbumPlaycount { get; set; }
}
