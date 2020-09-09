namespace FMBot.LastFM.Domain.Types
{
    public class Call
    {
        public const string
            ArtistInfo = "Artist.getInfo",
            AlbumInfo = "Album.getInfo",
            TrackInfo = "Track.getInfo",
            TrackLove = "track.love",
            TrackUnLove = "track.unlove",
            UserInfo = "User.getInfo",
            TopTracks = "user.gettoptracks",
            GetToken = "auth.GetToken",
            GetAuthSession = "auth.getSession";
    }
}
