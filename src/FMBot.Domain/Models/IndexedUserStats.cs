namespace FMBot.Domain.Models;

public class IndexedUserStats
{
    public long ArtistCount { get; set; }
    public long AlbumCount { get; set; }
    public long TrackCount { get; set; }
    public long PlayCount { get; set; }
    public long? ImportCount { get; set; }
    public long? TotalCount { get; set; }
}
