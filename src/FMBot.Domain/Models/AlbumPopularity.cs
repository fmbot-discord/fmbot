namespace FMBot.Domain.Models;

public class AlbumPopularity
{
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public int Popularity { get; set; }
    public long Playcount { get; set; }
}
