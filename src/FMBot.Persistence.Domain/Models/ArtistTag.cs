namespace FMBot.Persistence.Domain.Models;

public class ArtistTag
{
    public int ArtistId { get; set; }

    public int TagId { get; set; }

    public Artist Artist { get; set; }

    public Tag Tag { get; set; }
}
