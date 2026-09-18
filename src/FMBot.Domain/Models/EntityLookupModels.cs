namespace FMBot.Domain.Models;

public class EntityImageUrl
{
    public int? Id { get; set; }
    public string ArtistName { get; set; }
    public string Name { get; set; }
    public string AlbumName { get; set; }
    public string ImageUrl { get; set; }
}

public class EntityPlaycount
{
    public string ArtistName { get; set; }
    public string Name { get; set; }
    public long Playcount { get; set; }
}
