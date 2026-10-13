using FMBot.Domain.Enums;

namespace FMBot.Domain.Models;

public class IndexedUserStats
{
    public long? ArtistCount { get; set; }
    public long? AlbumCount { get; set; }
    public long? TrackCount { get; set; }
    public long? PlayCount { get; set; }
    public long? ImportCount { get; set; }
    public long? TotalCount { get; set; }

    public bool? UpdateError { get; set; }
    public UpdateType FailedUpdates { get; set; }

    public UpdateType CompletedUpdates { get; set; }
    public long? PlaysFetched { get; set; }
    public long? PlaysExpected { get; set; }
}
