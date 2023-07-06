using System;
using System.IO;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Interfaces;

namespace FMBot.Bot.Factories;

public class DataSourceFactory : IDataSourceFactory
{
    private readonly ILastfmRepository _lastfmRepository;
    private readonly IPlayDataSourceRepository _playDataSourceRepository;
    private readonly UserService _userService;

    public DataSourceFactory(ILastfmRepository lastfmRepository, IPlayDataSourceRepository playDataSourceRepository, UserService userService)
    {
        this._lastfmRepository = lastfmRepository;
        this._playDataSourceRepository = playDataSourceRepository;
        this._userService = userService;
    }

    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2, bool useCache = false, string sessionKey = null,
        long? fromUnixTimestamp = null, int amountOfPages = 1)
    {
        var importUser = await this._userService.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null)
        {
            return await this._playDataSourceRepository.GetRecentTracksAsync(importUser, count, useCache, sessionKey,
                fromUnixTimestamp, amountOfPages);
        }

        return await this._lastfmRepository.GetRecentTracksAsync(lastFmUserName, count, useCache, sessionKey,
            fromUnixTimestamp, amountOfPages);
    }

    public async Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? from = null, string sessionKey = null,
        long? until = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey, long totalScrobbles, long milestoneScrobble)
    {
        throw new NotImplementedException();
    }

    public async Task<DataSourceUser> GetLfmUserInfoAsync(string lastFmUserName)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, string username = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName, TimePeriod timePeriod, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(string lastFmUserName, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName, TimeSettingsModel timeSettings, long count = 2, long amountOfPages = 1)
    {
        var importUser = await this._userService.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null)
        {
            return await this._playDataSourceRepository.GetTopArtistsAsync(importUser, timeSettings, count, amountOfPages);
        }

        return await this._lastfmRepository.GetTopArtistsAsync(lastFmUserName, timeSettings, count, amountOfPages);
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName, TimePeriod timePeriod, long count = 2, long amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(string lastFmUserName, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName, string period, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(string lastFmUserName, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<RecentTrackList>> GetLovedTracksAsync(string lastFmUserName, int count = 2, string sessionKey = null,
        long? fromUnixTimestamp = null)
    {
        return await this._lastfmRepository.GetLovedTracksAsync(lastFmUserName, count, sessionKey, fromUnixTimestamp);
    }

    public async Task<MemoryStream> GetAlbumImageAsStreamAsync(string imageUrl)
    {
        return await this._lastfmRepository.GetAlbumImageAsStreamAsync(imageUrl);
    }

    public async Task<bool> LastFmUserExistsAsync(string lastFmUserName)
    {
        return await this._lastfmRepository.LastFmUserExistsAsync(lastFmUserName);
    }

    public async Task<Response<TokenResponse>> GetAuthToken()
    {
        return await this._lastfmRepository.GetAuthToken();
    }

    public async Task<Response<AuthSessionResponse>> GetAuthSession(string token)
    {
        return await this._lastfmRepository.GetAuthSession(token);
    }

    public async Task<bool> LoveTrackAsync(string lastFmSessionKey, string artistName, string trackName)
    {
        return await this._lastfmRepository.LoveTrackAsync(lastFmSessionKey, artistName, trackName);
    }

    public async Task<bool> UnLoveTrackAsync(string lastFmSessionKey, string artistName, string trackName)
    {
        return await this._lastfmRepository.UnLoveTrackAsync(lastFmSessionKey, artistName, trackName);
    }

    public async Task<Response<StoredPlayResponse>> SetNowPlayingAsync(string lastFmSessionKey, string artistName, string trackName, string albumName = null)
    {
        return await this._lastfmRepository.SetNowPlayingAsync(lastFmSessionKey, artistName, trackName, albumName);
    }

    public async Task<Response<StoredPlayResponse>> ScrobbleAsync(string lastFmSessionKey, string artistName, string trackName, string albumName = null)
    {
        return await this._lastfmRepository.ScrobbleAsync(lastFmSessionKey, artistName, trackName, albumName);
    }
}
