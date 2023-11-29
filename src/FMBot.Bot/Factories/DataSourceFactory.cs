using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace FMBot.Bot.Factories;

public class DataSourceFactory : IDataSourceFactory
{
    private readonly ILastfmRepository _lastfmRepository;
    private readonly IPlayDataSourceRepository _playDataSourceRepository;
    private readonly TimeService _timeService;
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly AliasService _aliasService;

    public DataSourceFactory(ILastfmRepository lastfmRepository,
        IPlayDataSourceRepository playDataSourceRepository,
        TimeService timeService,
        IMemoryCache cache,
        IDbContextFactory<FMBotDbContext> contextFactory, AliasService aliasService)
    {
        this._lastfmRepository = lastfmRepository;
        this._playDataSourceRepository = playDataSourceRepository;
        this._timeService = timeService;
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._aliasService = aliasService;
    }

    public async Task<User> GetUserAsync(string userNameLastFm)
    {
        var lastFmCacheKey = UserService.UserLastFmCacheKey(userNameLastFm);

        if (this._cache.TryGetValue(lastFmCacheKey, out User user))
        {
            return user;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var userNameParameter = new NpgsqlParameter("userNameLastFm", userNameLastFm);

        user = await db.Users
            .FromSql($"SELECT * FROM users WHERE UPPER(user_name_last_fm) = UPPER({userNameParameter}) AND last_used IS NOT NULL ORDER BY last_used DESC LIMIT 1")
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (user != null)
        {
            var discordUserIdCacheKey = UserService.UserDiscordIdCacheKey(user.DiscordUserId);

            this._cache.Set(discordUserIdCacheKey, user, TimeSpan.FromSeconds(5));
            this._cache.Set(lastFmCacheKey, user, TimeSpan.FromSeconds(5));
        }

        return user;
    }

    public async Task<ImportUser> GetImportUserForLastFmUserName(string lastFmUserName)
    {
        if (lastFmUserName == null)
        {
            return null;
        }

        var user = await GetUserAsync(lastFmUserName);

        if (user != null &&
            user.DataSource != DataSource.LastFm &&
            user.UserType != UserType.User)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var lastImportPlay = await db.Database
                .SqlQuery<DateTime?>($"SELECT time_played FROM user_plays WHERE play_source != 0 AND user_id = {user.UserId} ORDER BY time_played DESC LIMIT 1")
                .ToListAsync();

            if (lastImportPlay.Any())
            {
                return new ImportUser
                {
                    DiscordUserId = user.DiscordUserId,
                    DataSource = user.DataSource,
                    UserNameLastFM = user.UserNameLastFM,
                    UserId = user.UserId,
                    LastImportPlay = lastImportPlay.First()
                };
            }
        }

        return null;
    }

    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2, bool useCache = false, string sessionKey = null,
        long? fromUnixTimestamp = null, int amountOfPages = 1)
    {
        var recentTracks = await this._lastfmRepository.GetRecentTracksAsync(lastFmUserName, count, useCache, sessionKey,
            fromUnixTimestamp, amountOfPages);

        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null && recentTracks.Success && recentTracks.Content != null)
        {
            var total = await this._playDataSourceRepository.GetScrobbleCountFromDateAsync(importUser, fromUnixTimestamp);

            if (total.HasValue)
            {
                recentTracks.Content.TotalAmount = total.Value;
            }
        }

        return recentTracks;
    }

    public async Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? from = null, string sessionKey = null,
        long? until = null)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null)
        {
            return await this._playDataSourceRepository.GetScrobbleCountFromDateAsync(importUser, from, until);
        }

        return await this._lastfmRepository.GetScrobbleCountFromDateAsync(lastFmUserName, from, sessionKey, until);
    }

    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey, long totalScrobbles, int milestoneScrobble)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null)
        {
            return await this._playDataSourceRepository.GetMilestoneScrobbleAsync(importUser, milestoneScrobble);
        }

        return await this._lastfmRepository.GetMilestoneScrobbleAsync(lastFmUserName, sessionKey, totalScrobbles, milestoneScrobble);
    }

    public async Task<DataSourceUser> GetLfmUserInfoAsync(string lastFmUserName)
    {
        var user = await this._lastfmRepository.GetLfmUserInfoAsync(lastFmUserName);

        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null && user != null)
        {
            return await this._playDataSourceRepository.GetLfmUserInfoAsync(importUser, user);
        }

        return user;
    }

    public async Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery)
    {
        return await this._lastfmRepository.SearchTrackAsync(searchQuery);
    }

    public async Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null, bool redirectsEnabled = true)
    {
        var artistAlias = await this._aliasService.GetAlias(artistName);

        if (artistAlias != null && artistAlias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls) && redirectsEnabled)
        {
            redirectsEnabled = false;
        }

        var track = await this._lastfmRepository.GetTrackInfoAsync(trackName, artistName, redirectsEnabled, username);

        var importUser = await this.GetImportUserForLastFmUserName(username);

        if (importUser != null && track.Success && track.Content != null)
        {
            track.Content.UserPlaycount = await this._playDataSourceRepository.GetTrackPlaycount(importUser, trackName, artistName);
        }

        return track;
    }

    public async Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username, bool redirectsEnabled = true)
    {
        var artistAlias = await this._aliasService.GetAlias(artistName);

        if (artistAlias != null && artistAlias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls) && redirectsEnabled)
        {
            redirectsEnabled = false;
        }

        var artist = await this._lastfmRepository.GetArtistInfoAsync(artistName, username, redirectsEnabled);

        var importUser = await this.GetImportUserForLastFmUserName(username);

        if (importUser != null && artist.Success && artist.Content != null)
        {
            artist.Content.UserPlaycount = await this._playDataSourceRepository.GetArtistPlaycount(importUser, artistName);
        }

        return artist;
    }

    public async Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, string username = null, bool redirectsEnabled = true)
    {
        var artistAlias = await this._aliasService.GetAlias(artistName);

        if (artistAlias != null && artistAlias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls) && redirectsEnabled)
        {
            redirectsEnabled = false;
        }

        var album = await this._lastfmRepository.GetAlbumInfoAsync(artistName, albumName, redirectsEnabled, username);

        var importUser = await this.GetImportUserForLastFmUserName(username);

        if (importUser != null && album.Success && album.Content != null)
        {
            album.Content.UserPlaycount = await this._playDataSourceRepository.GetAlbumPlaycount(importUser, artistName, albumName);
        }

        return album;
    }

    public async Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery)
    {
        return await this._lastfmRepository.SearchAlbumAsync(searchQuery);
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);
        Response<TopAlbumList> topAlbums;

        if (importUser != null && timeSettings.StartDateTime < importUser.LastImportPlay)
        {
            topAlbums = await this._playDataSourceRepository.GetTopAlbumsAsync(importUser, timeSettings, count * amountOfPages);
            AddAlbumTopList(topAlbums, lastFmUserName);
            return topAlbums;
        }

        topAlbums = await this._lastfmRepository.GetTopAlbumsAsync(lastFmUserName, timeSettings, count, amountOfPages);

        await CorrectTopAlbumNamesInternally(topAlbums);
        AddAlbumTopList(topAlbums, lastFmUserName);

        return topAlbums;
    }

    private void AddAlbumTopList(Response<TopAlbumList> topAlbums, string lastFmUserName)
    {
        topAlbums.TopList = topAlbums.Content?.TopAlbums?.Select(s => new TopListObject
        {
            LastFMUsername = lastFmUserName,
            Name = s.AlbumName,
            SubName = s.ArtistName,
            Playcount = s.UserPlaycount.GetValueOrDefault()
        }).ToList();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(string lastFmUserName, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null && startDateTime < importUser.LastImportPlay)
        {
            return await this._playDataSourceRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(importUser, startDateTime, endDateTime, count);
        }

        var topAlbums = await this._lastfmRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(lastFmUserName, startDateTime, endDateTime, count);

        await CorrectTopAlbumNamesInternally(topAlbums);

        return topAlbums;
    }

    private async Task CorrectTopAlbumNamesInternally(Response<TopAlbumList> topAlbums)
    {
        if (topAlbums.Success && topAlbums.Content?.TopAlbums != null && topAlbums.Content.TopAlbums.Any())
        {
            foreach (var topAlbum in topAlbums.Content.TopAlbums)
            {
                var alias = await this._aliasService.GetDataCorrectionAlias(topAlbum.ArtistName);
                if (alias?.ArtistName != null)
                {
                    topAlbum.ArtistName = alias.ArtistName;
                }
            }
        }
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        Response<TopArtistList> topArtists;
        if (importUser != null && timeSettings.StartDateTime < importUser.LastImportPlay)
        {
            topArtists = await this._playDataSourceRepository.GetTopArtistsAsync(importUser, timeSettings, count * amountOfPages);
            AddArtistTopList(topArtists, lastFmUserName);
            return topArtists;
        }

        topArtists = await this._lastfmRepository.GetTopArtistsAsync(lastFmUserName, timeSettings, count, amountOfPages);

        await CorrectTopArtistNamesInternally(topArtists);
        AddArtistTopList(topArtists, lastFmUserName);

        return topArtists;
    }

    public async Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(string lastFmUserName, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        if (importUser != null && startDateTime < importUser.LastImportPlay)
        {
            return await this._playDataSourceRepository.GetTopArtistsForCustomTimePeriodAsync(importUser, startDateTime, endDateTime, count);
        }

        var topArtists = await this._lastfmRepository.GetTopArtistsForCustomTimePeriodAsync(lastFmUserName, startDateTime, endDateTime, count);

        await CorrectTopArtistNamesInternally(topArtists);

        return topArtists;
    }

    private async Task CorrectTopArtistNamesInternally(Response<TopArtistList> topArtists)
    {
        if (topArtists.Success && topArtists.Content?.TopArtists != null && topArtists.Content.TopArtists.Any())
        {
            foreach (var topArtist in topArtists.Content.TopArtists)
            {
                var alias = await this._aliasService.GetDataCorrectionAlias(topArtist.ArtistName);
                if (alias?.ArtistName != null)
                {
                    topArtist.ArtistName = alias.ArtistName;
                }
            }
        }
    }

    private void AddArtistTopList(Response<TopArtistList> topArtists, string lastFmUserName)
    {
        topArtists.TopList = topArtists.Content?.TopArtists?.Select(s => new TopListObject
        {
            LastFMUsername = lastFmUserName,
            Name = s.ArtistName,
            Playcount = s.UserPlaycount
        }).ToList();
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1, bool calculateTimeListened = false)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        Response<TopTrackList> topTracks;
        if (importUser != null && timeSettings.StartDateTime < importUser.LastImportPlay)
        {
            topTracks = await this._playDataSourceRepository.GetTopTracksAsync(importUser, timeSettings, count * amountOfPages);
        }
        else
        {
            topTracks = await this._lastfmRepository.GetTopTracksAsync(lastFmUserName, timeSettings, count, amountOfPages);
        }

        if (calculateTimeListened && topTracks.Success && topTracks.Content?.TopTracks != null && topTracks.Content.TopTracks.Any())
        {
            foreach (var topTrack in topTracks.Content.TopTracks)
            {
                var timeListened = await this._timeService.GetPlayTimeForTrackWithPlaycount(topTrack.ArtistName, topTrack.TrackName, topTrack.UserPlaycount.GetValueOrDefault(), topTrack.TimeListened);

                topTrack.TimeListened = new TopTimeListened
                {
                    TotalTimeListened = timeListened
                };
            }
        }

        topTracks.TopList = topTracks.Content?.TopTracks?.Select(s => new TopListObject
        {
            LastFMUsername = lastFmUserName,
            Name = s.TrackName,
            SubName = s.ArtistName,
            Playcount = s.UserPlaycount.GetValueOrDefault()
        }).ToList();

        return topTracks;
    }

    public async Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime,
        int count, bool calculateTimeListened = false)
    {
        var importUser = await this.GetImportUserForLastFmUserName(lastFmUserName);

        Response<TopTrackList> topTracks;
        if (importUser != null && startDateTime < importUser.LastImportPlay)
        {
            topTracks = await this._playDataSourceRepository.GetTopTracksForCustomTimePeriodAsyncAsync(importUser, startDateTime,
                endDateTime, count);
        }
        else
        {
            topTracks = await this._lastfmRepository.GetTopTracksForCustomTimePeriodAsyncAsync(lastFmUserName, startDateTime, endDateTime, count);
        }

        if (calculateTimeListened && topTracks.Success && topTracks.Content?.TopTracks != null && topTracks.Content.TopTracks.Any())
        {
            foreach (var topTrack in topTracks.Content.TopTracks)
            {
                var timeListened = await this._timeService.GetPlayTimeForTrackWithPlaycount(topTrack.ArtistName, topTrack.TrackName, topTrack.UserPlaycount.GetValueOrDefault(), topTrack.TimeListened);

                topTrack.TimeListened = new TopTimeListened
                {
                    TotalTimeListened = timeListened
                };
            }
        }

        return topTracks;
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

    public async Task<Response<bool>> SetNowPlayingAsync(string lastFmSessionKey, string artistName, string trackName, string albumName = null)
    {
        return await this._lastfmRepository.SetNowPlayingAsync(lastFmSessionKey, artistName, trackName, albumName);
    }

    public async Task<Response<StoredPlayResponse>> ScrobbleAsync(string lastFmSessionKey, string artistName, string trackName, string albumName = null, DateTime? timeStamp = null)
    {
        return await this._lastfmRepository.ScrobbleAsync(lastFmSessionKey, artistName, trackName, albumName, timeStamp);
    }
}
