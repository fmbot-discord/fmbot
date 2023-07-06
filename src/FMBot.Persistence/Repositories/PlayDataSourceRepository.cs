using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Enums;
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

    private static ICollection<UserPlayTs> GetFinalUserPlays(User user, ICollection<UserPlayTs> userPlays)
    {
        switch (user.DataSource)
        {
            case DataSource.FullSpotifyThenLastFm:
                {
                    var lastSpotifyPlay = userPlays
                        .Where(w => w.PlaySource == PlaySource.SpotifyImport)
                        .MaxBy(o => o.TimePlayed);

                    return userPlays
                        .Where(w => w.PlaySource != PlaySource.LastFm || w.TimePlayed > lastSpotifyPlay.TimePlayed)
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

    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(User user, int count = 2, bool useCache = false, string sessionKey = null,
        long? fromUnixTimestamp = null, int amountOfPages = 1)
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

    public async Task<long?> GetScrobbleCountFromDateAsync(User user, long? from = null, string sessionKey = null,
        long? until = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow);
        plays = GetFinalUserPlays(user, plays);

        return plays.Count;
    }

    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(User user, string sessionKey, long totalScrobbles, long milestoneScrobble)
    {
        throw new NotImplementedException();
    }

    public async Task<DataSourceUser> GetLfmUserInfoAsync(User user)
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

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(User user, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(User user, TimePeriod timePeriod, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(User user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(User user, TimeSettingsModel timeSettings, long count = 2, long amountOfPages = 1)
    {
        if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue || !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            return await GetTopArtistsAsync(user, timeSettings.TimePeriod, timeSettings.PlayDays.GetValueOrDefault(), count, amountOfPages);
        }

        return await GetTopArtistsForCustomTimePeriodAsync(user, timeSettings.StartDateTime.Value,
            timeSettings.EndDateTime.Value, (int)count);
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(User user, TimePeriod timePeriod, int playDays, long count = 2, long amountOfPages = 1)
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
                        UserPlaycount = s.Count()
                    }).ToList()
            }
        };
    }

    public async Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(User user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, startDateTime, endDateTime);
        plays = GetFinalUserPlays(user, plays);

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
                        UserPlaycount = s.Count()
                    }).ToList()
            }
        };
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(User user, TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(User user, string period, int count = 2, int amountOfPages = 1)
    {
        throw new NotImplementedException();
    }

    public async Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(User user, DateTime startDateTime, DateTime endDateTime,
        int count)
    {
        throw new NotImplementedException();
    }
}
