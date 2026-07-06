namespace FMBot.Persistence.Domain.Models;

public class AlbumGenre
{
    public int Id { get; set; }

    public int AlbumId { get; set; }

    public string Name { get; set; }

    public Album Album { get; set; }
}
