namespace FMBot.Persistence.Domain.Models;

public class ArtistImage : Image
{
    public int ArtistId { get; set; }

    public Artist Artist { get; set; }
}
