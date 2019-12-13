namespace FMBot.LastFM.Models
{
    public class Call
    {
        public string ArtistInfo => "Artist.getInfo";

        public string AlbumInfo => "Album.getInfo";

        public string TrackInfo => "Track.getInfo";

        public string UserInfo => "User.getInfo";
    }
}
