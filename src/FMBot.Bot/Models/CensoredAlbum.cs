namespace FMBot.Bot.Models;

public class CensoredAlbum
{
    public CensoredAlbum(string artist, string album)
    {
        this.ArtistName = artist;
        this.AlbumName = album;
    }

    public string ArtistName { get; }

    public string AlbumName { get; }
}
