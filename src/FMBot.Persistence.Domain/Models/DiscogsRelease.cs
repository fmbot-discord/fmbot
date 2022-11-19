using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class DiscogsRelease
{
    public int DiscogsId { get; set; }
    public int? MasterId { get; set; }

    public int? AlbumId { get; set; }

    public string Title { get; set; }

    public string Artist { get; set; }
    public int? ArtistId { get; set; }
    public int ArtistDiscogsId { get; set; }

    public string FeaturingArtistJoin { get; set; }
    public string FeaturingArtist { get; set; }
    public int? FeaturingArtistId { get; set; }
    public int? FeaturingArtistDiscogsId { get; set; }

    public string CoverUrl { get; set; }
    public int? Year { get; set; }

    public string Format { get; set; }
    public string FormatText { get; set; }
    public List<DiscogsFormatDescriptions> FormatDescriptions { get; set; }

    public string Label { get; set; }
    public string SecondLabel { get; set; }

    public decimal? LowestPrice { get; set; }

    public List<UserDiscogsReleases> UserDiscogsReleases { get; set; }
    public List<DiscogsGenre> Genres { get; set; }
    public List<DiscogsStyle> Styles { get; set; }
}
