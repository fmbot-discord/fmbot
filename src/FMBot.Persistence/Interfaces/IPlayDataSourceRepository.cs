using System;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Persistence.Interfaces;

public interface IPlayDataSourceRepository
{
    Task<Response<RecentTrackList>> GetRecentTracksAsync(ImportUser user, int count = 2, bool useCache = false,
        string sessionKey = null, long? fromUnixTimestamp = null);

    Task<long?> GetScrobbleCountFromDateAsync(ImportUser user, long? from = null, string sessionKey = null,
        long? until = null);

    Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(ImportUser user, string sessionKey, long totalScrobbles,
        long milestoneScrobble);

    Task<DataSourceUser> GetLfmUserInfoAsync(ImportUser user, DataSourceUser dataSourceUser);
    Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery);
    Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null);
    Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username);
    Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, string username = null);
    Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(ImportUser user,
        TimeSettingsModel timeSettings, int count = 2);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(ImportUser user,
        TimePeriod timePeriod, int playDays, int count = 2);

    Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(ImportUser user,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopArtistList>> GetTopArtistsAsync(ImportUser user,
        TimeSettingsModel timeSettings, int count = 2);

    Task<Response<TopArtistList>> GetTopArtistsAsync(ImportUser user,
        TimePeriod timePeriod, int playDays, int count = 2);

    Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(ImportUser user,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopTrackList>> GetTopTracksAsync(ImportUser user,
        TimeSettingsModel timeSettings, int count = 2);

    Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(ImportUser user,
        DateTime startDateTime, DateTime endDateTime, int count);
}
