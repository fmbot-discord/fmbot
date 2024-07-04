using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class Artist
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string LastFmUrl { get; set; }
    public string LastFmDescription { get; set; }
    public DateTime? LastfmDate { get; set; }

    public string SpotifyImageUrl { get; set; }
    public DateTime? SpotifyImageDate { get; set; }
    public string SpotifyId { get; set; }
    public int? AppleMusicId { get; set; }

    public int? Popularity { get; set; }

    public Guid? Mbid { get; set; }
    public DateTime? MusicBrainzDate { get; set; }
    public string Location { get; set; }
    public string CountryCode { get; set; }
    public string Type { get; set; }
    public string Gender { get; set; }
    public string Disambiguation { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string AppleMusicUrl { get; set; }
    public DateTime? AppleMusicDate { get; set; }

    public ICollection<Track> Tracks { get; set; }
    public ICollection<Album> Albums { get; set; }
    public ICollection<ArtistAlias> ArtistAliases { get; set; }
    public ICollection<ArtistGenre> ArtistGenres { get; set; }
    public ICollection<ArtistLink> ArtistLinks { get; set; }
    public ICollection<ArtistImage> Images { get; set; }

}
