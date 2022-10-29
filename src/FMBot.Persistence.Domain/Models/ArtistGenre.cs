namespace FMBot.Persistence.Domain.Models;

public class ArtistGenre
{
    public int Id { get; set; }

    public int ArtistId { get; set; }

    public string Name { get; set; }

    public Artist Artist { get; set; }
}
