namespace FMBot.Persistence.Domain.Models
{
    public class CensoredMusic
    {
        public string ArtistName { get; set; }

        public string AlbumName { get; set; }

        public bool SafeForCommands { get; set; }

        public bool SafeForFeatured { get; set; }

        public bool Artist { get; set; }
    }
}
