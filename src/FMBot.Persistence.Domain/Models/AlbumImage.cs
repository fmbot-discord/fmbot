namespace FMBot.Persistence.Domain.Models;

public class AlbumImage : Image
{
    public int AlbumId { get; set; }

    public Album Album { get; set; }
}
