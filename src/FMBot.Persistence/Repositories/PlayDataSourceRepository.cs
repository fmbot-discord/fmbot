using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public class PlayDataSourceRepository : IPlayDataSourceRepository
{
    private readonly BotSettings _botSettings;

    public PlayDataSourceRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    private static ICollection<UserPlayTs> GetFinalUserPlays(ImportUser user, ICollection<UserPlayTs> userPlays)
    {
        switch (user.DataSource)
        {
            case DataSource.FullSpotifyThenLastFm:
                {
                    return userPlays
                        .Where(w => w.PlaySource != PlaySource.LastFm || w.TimePlayed > user.LastImportPlay)
                        .ToList();
                }
            case DataSource.SpotifyThenFullLastFm:
                {
                    var firstLastFmPlay = userPlays
                        .Where(w => w.PlaySource == PlaySource.LastFm)
                        .MinBy(o => o.TimePlayed);

                    return userPlays
                        .Where(w => w.PlaySource != PlaySource.SpotifyImport || w.TimePlayed < firstLastFmPlay.TimePlayed)
                        .ToList();
                }
            default:
                return userPlays;
        }
    }

    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(ImportUser user, int count = 2, bool useCache = false, string sessionKey = null,
        long? fromUnixTimestamp = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlays(user.UserId, connection, count);
        plays = GetFinalUserPlays(user, plays);

        return new Response<RecentTrackList>
        {
            Success = true,
            Content = new RecentTrackList
            {
                RecentTracks = plays.Select(s => new RecentTrack
                {
                    TimePlayed = s.TimePlayed,
                    ArtistName = s.ArtistName,
                    AlbumName = s.AlbumName,
                    TrackName = s.TrackName
                }).ToList()
            }
        };
    }

    public async Task<long?> GetScrobbleCountFromDateAsync(ImportUser user, long? from = null, string sessionKey = null,
        long? until = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow);
        plays = GetFinalUserPlays(user, plays);

        return plays.Count;
    }

    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(ImportUser user, string sessionKey, long totalScrobbles, long milestoneScrobble)
    {
        throw new NotImplementedException();
    }

    public async Task<DataSourceUser> GetLfmUserInfoAsync(ImportUser user)
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

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(ImportUser user, TimeSettingsModel timeSettings, int count = 2)
    {
        if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue || !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            return await GetTopAlbumsAsync(user, timeSettings.TimePeriod, timeSettings.PlayDays.GetValueOrDefault(), count);
        }

        return await GetTopAlbumsForCustomTimePeriodAsyncAsync(user, timeSettings.StartDateTime.Value,
            timeSettings.EndDateTime.Value, count);
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(ImportUser user, TimePeriod timePeriod, int playDays, int count = 2)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        ICollection<UserPlayTs> plays = new List<UserPlayTs>();
        if (timePeriod == TimePeriod.AllTime)
        {
            plays = await PlayRepository.GetUserPlays(user.UserId, connection, 99999999);
        }
        else
        {
            plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-playDays));
        }

        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopAlbums(plays, count);
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(ImportUser user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, startDateTime, endDateTime);
        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopAlbums(plays, count);
    }

    private static Response<TopAlbumList> PlaysToTopAlbums(IEnumerable<UserPlayTs> plays, int count)
    {
        return new Response<TopAlbumList>
        {
            Success = true,
            Content = new TopAlbumList
            {
                TopAlbums = plays
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        AlbumName = g.AlbumName.ToLower()
                    })
                    .Select(s => new TopAlbum
                    {
                        ArtistName = s.First().ArtistName,
                        UserPlaycount = s.Count(),
                        ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.First().ArtistName),
                        AlbumName = s.First().AlbumName,
                        AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(s.First().ArtistName, s.First().AlbumName)
                    })
                    .OrderByDescending(o => o.UserPlaycount)
                    .Take(count)
                    .ToList()
            }
        };
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(ImportUser user, TimeSettingsModel timeSettings, int count = 2)
    {
        if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue || !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            return await GetTopArtistsAsync(user, timeSettings.TimePeriod, timeSettings.PlayDays.GetValueOrDefault(), count);
        }

        return await GetTopArtistsForCustomTimePeriodAsync(user, timeSettings.StartDateTime.Value,
            timeSettings.EndDateTime.Value, count);
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(ImportUser user, TimePeriod timePeriod, int playDays, int count = 2)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        ICollection<UserPlayTs> plays = new List<UserPlayTs>();
        if (timePeriod == TimePeriod.AllTime)
        {
            plays = await PlayRepository.GetUserPlays(user.UserId, connection, 99999999);
        }
        else
        {
            plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-playDays));
        }

        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopArtists(plays, count);
    }

    public async Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(ImportUser user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, startDateTime, endDateTime);
        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopArtists(plays, count);
    }

    private static Response<TopArtistList> PlaysToTopArtists(IEnumerable<UserPlayTs> plays, int count)
    {
        return new Response<TopArtistList>
        {
            Success = true,
            Content = new TopArtistList
            {
                TopArtists = plays
                    .GroupBy(g => g.ArtistName.ToLower())
                    .Select(s => new TopArtist
                    {
                        ArtistName = s.First().ArtistName,
                        UserPlaycount = s.Count(),
                        ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.First().ArtistName)
                    })
                    .OrderByDescending(o => o.UserPlaycount)
                    .Take(count)
                    .ToList()
            }
        };
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(ImportUser user, TimeSettingsModel timeSettings, int count = 2)
    {
        if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue || !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            return await GetTopTracksAsync(user, timeSettings.TimePeriod, timeSettings.PlayDays.GetValueOrDefault(), count);
        }

        return await GetTopTracksForCustomTimePeriodAsyncAsync(user, timeSettings.StartDateTime.Value,
            timeSettings.EndDateTime.Value, count);
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(ImportUser user, TimePeriod timePeriod, int playDays, int count = 2)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        ICollection<UserPlayTs> plays = new List<UserPlayTs>();
        if (timePeriod == TimePeriod.AllTime)
        {
            plays = await PlayRepository.GetUserPlays(user.UserId, connection, 99999999);
        }
        else
        {
            plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-playDays));
        }

        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopTracks(plays, count);
    }

    public async Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(ImportUser user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, startDateTime, endDateTime);
        plays = GetFinalUserPlays(user, plays);

        return PlaysToTopTracks(plays, count);
    }

    private static Response<TopTrackList> PlaysToTopTracks(IEnumerable<UserPlayTs> plays, int count)
    {
        return new Response<TopTrackList>
        {
            Success = true,
            Content = new TopTrackList
            {
                TopTracks = plays
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        TrackName = g.TrackName.ToLower()
                    })
                    .Select(s => new TopTrack
                    {
                        ArtistName = s.First().ArtistName,
                        UserPlaycount = s.Count(),
                        ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.First().ArtistName),
                        TrackName = s.First().TrackName,
                        TrackUrl = LastfmUrlExtensions.GetTrackUrl(s.First().ArtistName, s.First().TrackName),
                        AlbumName = s.First().AlbumName
                    })
                    .OrderByDescending(o => o.UserPlaycount)
                    .Take(count)
                    .ToList()
            }
        };
    }
}
