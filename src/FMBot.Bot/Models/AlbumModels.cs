namespace FMBot.Bot.Models
{
    public class AlbumSearchModel
    {
        public AlbumSearchModel(bool albumFound,
            string artist = null,
            string artistUrl = null,
            string name = null,
            string url = null)
        {
            this.Artist = artist;
            this.ArtistUrl = artistUrl;
            this.Name = name;
            this.AlbumFound = albumFound;
            this.Url = url;
        }

        public bool AlbumFound { get; }

        public string Name { get; }

        public string Artist { get; }

        public string ArtistUrl { get; }

        public string Url { get; }
    }
}
