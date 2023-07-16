using System;
using System.Collections.Generic;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models;

public class DailyOverview
{
    public int Uniques { get; set; }

    public int Playcount { get; set; }

    public double AvgPerDay { get; set; }

    public List<DayOverview> Days { get; set; }
}

public class DayOverview
{
    public DateTime Date { get; set; }

    public int Playcount { get; set; }

    public string TopArtist { get; set; }

    public string TopTrack { get; set; }

    public string TopAlbum { get; set; }

    public List<string> TopGenres { get; set; }

    public TimeSpan ListeningTime { get; set; }

    public List<UserPlay> Plays { get; set; }
}

public class YearOverview
{
    public bool LastfmErrors { get; set; }
    public int Year { get; set; }

    public TopArtistList TopArtists { get; set; }
    public TopAlbumList TopAlbums { get; set; }
    public TopTrackList TopTracks { get; set; }

    public TopArtistList PreviousTopArtists { get; set; }
    public TopAlbumList PreviousTopAlbums { get; set; }
    public TopTrackList PreviousTopTracks { get; set; }
}
