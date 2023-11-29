using System;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Persistence.Interfaces;

public interface IPlayDataSourceRepository
{
    Task<Response<RecentTrackList>> GetRecentTracksAsync(ImportUser user, int count = 2, bool useCache = false, long? fromUnixTimestamp = null);

    Task<long?> GetScrobbleCountFromDateAsync(ImportUser user, long? from = null, long? until = null);

    Task<long?> GetStoredPlayCountBeforeDateAsync(User user, DateTime until);

    Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(ImportUser user, 
        int milestoneScrobble);

    Task<DataSourceUser> GetLfmUserInfoAsync(ImportUser user, DataSourceUser dataSourceUser);
    Task<int?> GetTrackPlaycount(ImportUser user, string trackName, string artistName);
    Task<int?> GetArtistPlaycount(ImportUser user, string artistName);
    Task<int?> GetAlbumPlaycount(ImportUser user, string artistName, string albumName);

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
