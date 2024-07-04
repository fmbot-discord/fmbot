using System;

namespace FMBot.Persistence.Domain.Models;

public class Track
{
    public int Id { get; set; }

    public string Name { get; set; }

    public int? ArtistId { get; set; }

    public int? AlbumId { get; set; }
    public Guid? Mbid { get; set; }
    public string Isrc { get; set; }
    public string SpotifyId { get; set; }
    public int? AppleMusicId { get; set; }

    public string LastFmUrl { get; set; }
    public string LastFmDescription { get; set; }
    public DateTime? LastfmDate { get; set; }

    public string ArtistName { get; set; }
    public string AlbumName { get; set; }

    public float? Danceability { get; set; }
    public float? Energy { get; set; }
    public int? Key { get; set; }
    public float? Loudness { get; set; }
    public float? Speechiness { get; set; }
    public float? Acousticness { get; set; }
    public float? Instrumentalness { get; set; }
    public float? Liveness { get; set; }
    public float? Valence { get; set; }
    public float? Tempo { get; set; }

    public int? Popularity { get; set; }
    public int? DurationMs { get; set; }

    public string AppleMusicUrl { get; set; }
    public string AppleMusicTagline { get; set; }
    public string AppleMusicDescription { get; set; }
    public string AppleMusicShortDescription { get; set; }

    public string SpotifyPreviewUrl { get; set; }
    public string AppleMusicPreviewUrl { get; set; }
    
    public DateTime? AppleMusicDate { get; set; }
    public DateTime? SpotifyLastUpdated { get; set; }

    public Artist Artist { get; set; }
    public Album Album { get; set; }
}
