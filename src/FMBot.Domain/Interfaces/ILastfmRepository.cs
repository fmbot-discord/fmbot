using System;
using System.IO;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;

namespace FMBot.Domain.Interfaces;

public interface ILastfmRepository
{
    Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2, bool useCache = false,
    string sessionKey = null, long? fromUnixTimestamp = null, int amountOfPages = 1);

    Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? from = null, string sessionKey = null,
        long? until = null);

    Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey, long totalScrobbles,
        long milestoneScrobble);

    Task<DataSourceUser> GetLfmUserInfoAsync(string lastFmUserName);
    Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery);
    Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, bool redirectsEnabled, string username = null);
    Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username, bool redirectsEnabled);
    Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, bool redirectsEnabled, string username = null);
    Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName,
        TimePeriod timePeriod, int count = 2, int amountOfPages = 1);

    Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, long count = 2, long amountOfPages = 1);

    Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName,
        TimePeriod timePeriod, long count = 2, long amountOfPages = 1);

    Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName,
        string period, int count = 2, int amountOfPages = 1);

    Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count);

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
