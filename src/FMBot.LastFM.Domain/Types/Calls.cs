namespace FMBot.LastFM.Domain.Types
{
    public class Call
    {
        public const string
            ArtistInfo = "Artist.getInfo",
            AlbumInfo = "Album.getInfo",
            TrackInfo = "Track.getInfo",
            TrackLove = "track.love",
            UserInfo = "User.getInfo",
            TopTracks = "user.gettoptracks",
            GetToken = "auth.GetToken",
            GetAuthSession = "auth.getSession";
    }
}
