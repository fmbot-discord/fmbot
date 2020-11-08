namespace FMBot.LastFM.Domain.Types
{
    public class Call
    {
        public static readonly string
            ArtistInfo = "Artist.getInfo",
            AlbumInfo = "Album.getInfo",
            TrackInfo = "Track.getInfo",
            TrackLove = "track.love",
            TrackUnLove = "track.unLove",
            UserInfo = "User.getInfo",
            TopTracks = "user.getTopTracks",
            RecentTracks = "user.getRecentTracks",
            GetToken = "auth.GetToken",
            GetAuthSession = "auth.getSession";
    }
}
