using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class DiscogsMaster
{
    public int DiscogsId { get; set; }

    public string Title { get; set; }

    public string Artist { get; set; }
    public int? ArtistId { get; set; }
    public int ArtistDiscogsId { get; set; }

    public string FeaturingArtistJoin { get; set; }
    public string FeaturingArtist { get; set; }
    public int? FeaturingArtistId { get; set; }
    public int? FeaturingArtistDiscogsId { get; set; }

    public string Country { get; set; }

    public List<DiscogsRelease> Releases { get; set; }
    public List<DiscogsGenre> Genres { get; set; }
    public List<DiscogsStyle> Styles { get; set; }
}
