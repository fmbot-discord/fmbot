using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class Album
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public string LastFmUrl { get; set; }
    public string LastFmDescription { get; set; }
    public DateTime? LastfmDate { get; set; }

    public Guid? Mbid { get; set; }

    public string SpotifyImageUrl { get; set; }

    public string LastfmImageUrl { get; set; }

    public DateTime? SpotifyImageDate { get; set; }

    public string SpotifyId { get; set; }

    public int? Popularity { get; set; }

    public string Label { get; set; }

    public string ReleaseDate { get; set; }

    public string ReleaseDatePrecision { get; set; }

    public int? ArtistId { get; set; }

    public Artist Artist { get; set; }

    public ICollection<Track> Tracks { get; set; }
}
