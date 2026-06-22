namespace FMBot.Persistence.Domain.Models;

public class TrackTag
{
    public int TrackId { get; set; }

    public int TagId { get; set; }

    public Track Track { get; set; }

    public Tag Tag { get; set; }
}
