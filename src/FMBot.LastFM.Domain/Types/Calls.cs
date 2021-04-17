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
            TrackScrobble = "track.scrobble",
            TrackUpdateNowPlaying = "track.updatenowplaying",
            UserInfo = "User.getInfo",
            TopTracks = "user.getTopTracks",
            RecentTracks = "user.getRecentTracks",
            LovedTracks = "user.getLovedTracks",
            GetToken = "auth.GetToken",
            GetAuthSession = "auth.getSession";
    }
}
