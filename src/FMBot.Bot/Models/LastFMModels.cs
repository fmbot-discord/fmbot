namespace FMBot.Bot.Models
{
    public class Album
    {
        public Album(string artist, string album)
        {
            this.ArtistName = artist;
            this.AlbumName = album;
        }

        public string ArtistName { get; }

        public string AlbumName { get; }
    }
}
