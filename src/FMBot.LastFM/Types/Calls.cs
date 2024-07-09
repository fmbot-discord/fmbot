namespace FMBot.LastFM.Types;

public class Call
{
    public static readonly string
        ArtistInfo = "artist.getInfo",
        AlbumInfo = "album.getInfo",
        TrackInfo = "track.getInfo",
        TrackLove = "track.love",
        TrackUnLove = "track.unlove",
        TrackScrobble = "track.scrobble",
        TrackUpdateNowPlaying = "track.updateNowPlaying",
        UserInfo = "user.getInfo",
        TopTracks = "user.getTopTracks",
        RecentTracks = "user.getRecentTracks",
        LovedTracks = "user.getLovedTracks",
        GetWeeklyArtistChart = "user.getWeeklyArtistChart",
        GetWeeklyAlbumChart = "user.getWeeklyAlbumChart",
        GetWeeklyTrackChart = "user.getWeeklyTrackChart",
        GetToken = "auth.getToken",
        GetAuthSession = "auth.getSession";
}
