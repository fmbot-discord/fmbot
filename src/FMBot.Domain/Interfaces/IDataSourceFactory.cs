using System;
using System.IO;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;

namespace FMBot.Domain.Interfaces;

public interface IDataSourceFactory
{
    Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2, bool useCache = false,
        string sessionKey = null, long? fromUnixTimestamp = null, int amountOfPages = 1);

    Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? from = null, string sessionKey = null,
        long? until = null);

    Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey, long totalScrobbles,
        int milestoneScrobble);

    Task<DataSourceUser> GetLfmUserInfoAsync(string lastFmUserName);
    Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery);
    Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null, bool redirectsEnabled = true);
    Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username, bool redirectsEnabled = true);
    Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, string username = null, bool redirectsEnabled = true);
    Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1, bool calculateTimeListened = false);

    Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count, bool calculateTimeListened = false);

    Task<Response<RecentTrackList>> GetLovedTracksAsync(string lastFmUserName, int count = 2, string sessionKey = null, long? fromUnixTimestamp = null);
    Task<MemoryStream> GetAlbumImageAsStreamAsync(string imageUrl);
    Task<bool> LastFmUserExistsAsync(string lastFmUserName);
    Task<Response<TokenResponse>> GetAuthToken();
    Task<Response<AuthSessionResponse>> GetAuthSession(string token);
    Task<bool> LoveTrackAsync(string lastFmSessionKey, string artistName, string trackName);
    Task<bool> UnLoveTrackAsync(string lastFmSessionKey, string artistName, string trackName);
    Task<Response<bool>> SetNowPlayingAsync(string lastFmSessionKey, string artistName, string trackName,
        string albumName = null);
    Task<Response<StoredPlayResponse>> ScrobbleAsync(string lastFmSessionKey, string artistName, string trackName, string albumName = null, DateTime? timeStamp = null);

}
