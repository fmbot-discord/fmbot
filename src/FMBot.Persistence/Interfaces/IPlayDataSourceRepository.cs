using System;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Persistence.Interfaces;

public interface IPlayDataSourceRepository
{
    Task<Response<RecentTrackList>> GetRecentTracksAsync(User user, int count = 2, bool useCache = false,
        string sessionKey = null, long? fromUnixTimestamp = null, int amountOfPages = 1);

    Task<long?> GetScrobbleCountFromDateAsync(User user, long? from = null, string sessionKey = null,
        long? until = null);

    Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(User user, string sessionKey, long totalScrobbles,
        long milestoneScrobble);

    Task<DataSourceUser> GetLfmUserInfoAsync(User user);
    Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery);
    Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null);
    Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username);
    Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, string username = null);
    Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(User user,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopAlbumList>> GetTopAlbumsAsync(User user,
        TimePeriod timePeriod, int count = 2, int amountOfPages = 1);

    Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(User user,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopArtistList>> GetTopArtistsAsync(User user,
        TimeSettingsModel timeSettings, long count = 2, long amountOfPages = 1);

    Task<Response<TopArtistList>> GetTopArtistsAsync(User user,
        TimePeriod timePeriod, int playDays, long count = 2, long amountOfPages = 1);

    Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(User user,
        DateTime startDateTime, DateTime endDateTime, int count);

    Task<Response<TopTrackList>> GetTopTracksAsync(User user,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1);

    Task<Response<TopTrackList>> GetTopTracksAsync(User user,
        string period, int count = 2, int amountOfPages = 1);

    Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(User user,
        DateTime startDateTime, DateTime endDateTime, int count);
}
