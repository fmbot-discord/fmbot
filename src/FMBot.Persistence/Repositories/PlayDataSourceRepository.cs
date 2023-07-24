using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
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

    public static ICollection<UserPlay> GetFinalUserPlays(ImportUser user, ICollection<UserPlay> userPlays)
    {
        switch (user?.DataSource)
        {
            case DataSource.FullSpotifyThenLastFm:
                {
                    var firstImportPlay = userPlays.Where(w => w.PlaySource != PlaySource.LastFm).MinBy(o => o.TimePlayed)?.TimePlayed;

                    return userPlays
                        .Where(w => w.PlaySource != PlaySource.LastFm || w.TimePlayed > user.LastImportPlay || w.TimePlayed < firstImportPlay)
                        .ToList();
                }
            case DataSource.SpotifyThenFullLastFm:
                {
                    var firstLastFmPlay = userPlays
                        .Where(w => w.PlaySource == PlaySource.LastFm)
                        .MinBy(o => o.TimePlayed);

                    return userPlays
                        .Where(w => w.PlaySource != PlaySource.SpotifyImport || w.TimePlayed < (firstLastFmPlay?.TimePlayed ?? DateTime.UtcNow))
                        .ToList();
                }
            case DataSource.LastFm:
            default:
                {
                    return userPlays
                        .Where(w => w.PlaySource is null or PlaySource.LastFm)
                        .ToList();
                }
        }
    }

    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(ImportUser user, int count = 2, bool useCache = false, long? fromUnixTimestamp = null)
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

    public async Task<long?> GetScrobbleCountFromDateAsync(ImportUser user, long? from = null, long? until = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var fromTimeStamp = from.HasValue ? DateTimeOffset.FromUnixTimeSeconds(from.Value).UtcDateTime : new DateTime(2000, 1, 1);

        var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, fromTimeStamp);
        plays = GetFinalUserPlays(user, plays);

        return plays.Count;
    }

    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(ImportUser user, int milestoneScrobble)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlays(user.UserId, connection, 99999999);

        plays = GetFinalUserPlays(user, plays);

        var milestone = plays
            .OrderBy(o => o.TimePlayed)
            .ElementAtOrDefault(milestoneScrobble - 1);

        return new Response<RecentTrack>
        {
            Content = milestone != null
                ? new RecentTrack
                {
                    AlbumName = milestone.AlbumName,
                    AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(milestone.ArtistName, milestone.AlbumName),
                    ArtistName = milestone.ArtistName,
                    ArtistUrl = LastfmUrlExtensions.GetArtistUrl(milestone.ArtistName),
                    TrackName = milestone.TrackName,
                    TrackUrl = LastfmUrlExtensions.GetTrackUrl(milestone.ArtistName, milestone.TrackName),
                    TimePlayed = milestone.TimePlayed
                }
                : null,
            Success = milestone != null
        };
    }

    public async Task<DataSourceUser> GetLfmUserInfoAsync(ImportUser user, DataSourceUser dataSourceUser)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var plays = await PlayRepository.GetUserPlays(user.UserId, connection, 99999999);

        plays = GetFinalUserPlays(user, plays);

        dataSourceUser.Playcount = plays.Count;

        dataSourceUser.ArtistCount = plays
            .GroupBy(g => g.ArtistName.ToLower()).Count();

        dataSourceUser.AlbumCount = plays
            .Where(w => w.AlbumName != null)
            .GroupBy(g => new
            {
                ArtistName = g.ArtistName.ToLower(),
                AlbumName = g.AlbumName.ToLower()
            }).Count();

        dataSourceUser.TrackCount = plays.GroupBy(g => new
        {
            ArtistName = g.ArtistName.ToLower(),
            TrackName = g.TrackName.ToLower()
        }).Count();

        return dataSourceUser;
    }

    public async Task<int?> GetTrackPlaycount(ImportUser user, string trackName, string artistName)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await TrackRepository.GetTrackPlayCountForUser(connection, artistName, trackName, user.UserId);
    }

    public async Task<int?> GetArtistPlaycount(ImportUser user, string artistName)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await ArtistRepository.GetArtistPlayCountForUser(connection, artistName, user.UserId);
    }

    public async Task<int?> GetAlbumPlaycount(ImportUser user, string artistName, string albumName)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.GetAlbumPlayCountForUser(connection, artistName, albumName, user.UserId);
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

        ICollection<UserPlay> plays = new List<UserPlay>();
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

    private static Response<TopAlbumList> PlaysToTopAlbums(IEnumerable<UserPlay> plays, int count)
    {
        return new Response<TopAlbumList>
        {
            Success = true,
            Content = new TopAlbumList
            {
                TopAlbums = plays
                    .Where(w => w.AlbumName != null)
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
                        AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(s.First().ArtistName, s.First().AlbumName),
                        FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed,
                        TimeListened = new TopTimeListened
                        {
                            MsPlayed = s.Sum(su => su.MsPlayed) ?? 0,
                            PlaysWithPlayTime = s.Count(wh => wh.MsPlayed != null),
                            CountedTracks = s.GroupBy(gr => gr.TrackName)
                                .Select(se =>
                                    new CountedTrack
                                    {
                                        Name = se.Key,
                                        CountedPlays = se.Count()
                                    }).ToList()
                        }
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

        ICollection<UserPlay> plays = new List<UserPlay>();
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

    private static Response<TopArtistList> PlaysToTopArtists(IEnumerable<UserPlay> plays, int count)
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
                        ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.First().ArtistName),
                        FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed,
                        TimeListened = new TopTimeListened
                        {
                            MsPlayed = s.Sum(su => su.MsPlayed) ?? 0,
                            PlaysWithPlayTime = s.Count(wh => wh.MsPlayed != null),
                            CountedTracks = s.GroupBy(gr => gr.TrackName)
                                .Select(se =>
                                    new CountedTrack
                                    {
                                        Name = se.Key,
                                        CountedPlays = se.Count()
                                    }).ToList()
                        }
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

        ICollection<UserPlay> plays = new List<UserPlay>();
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

    private static Response<TopTrackList> PlaysToTopTracks(IEnumerable<UserPlay> plays, int count)
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
                        AlbumName = s.First().AlbumName,
                        FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed,
                        TimeListened = new TopTimeListened
                        {
                            MsPlayed = s.Sum(su => su.MsPlayed) ?? 0,
                            PlaysWithPlayTime = s.Count(wh => wh.MsPlayed != null)
                        }
                    })
                    .OrderByDescending(o => o.UserPlaycount)
                    .Take(count)
                    .ToList()
            }
        };
    }
}
