using FMBot.Domain.Models;

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

    public class ListAlbum
    {
        public string ArtistName { get; set; }

        public string AlbumName { get; set; }

        public int TotalPlaycount { get; set; }

        public int ListenerCount { get; set; }
    }

    public class WhoKnowsAlbumDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }

        public string UserName { get; set; }
    }

    public class AlbumSpotifyCoverDto
    {
        public string LastFmUrl { get; set; }

        public string SpotifyImageUrl { get; set; }
    }

    public class WhoKnowsGlobalAlbumDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }

        public PrivacyLevel PrivacyLevel { get; set; }
    }
}
