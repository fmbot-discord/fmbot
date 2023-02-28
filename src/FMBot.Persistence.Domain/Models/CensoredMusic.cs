using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class CensoredMusic
{
    public int CensoredMusicId { get; set; }

    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public string AlternativeCoverUrl { get; set; }

    public int? TimesCensored { get; set; }

    public bool SafeForCommands { get; set; }

    public bool SafeForFeatured { get; set; }

    public bool? FeaturedBanOnly { get; set; }

    public bool Artist { get; set; }

    public CensorType CensorType { get; set; }
}
