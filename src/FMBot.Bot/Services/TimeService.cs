using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services;

public class TimeService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public TimeService(IMemoryCache cache, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public async Task<TimeSpan> GetPlayTimeForPlays(IEnumerable<UserPlay> plays)
    {
        await CacheAllTrackLengths();

        var totalMs = plays.Sum(userPlay => GetTrackLengthForTrack(userPlay.ArtistName, userPlay.TrackName));

        return TimeSpan.FromMilliseconds(totalMs);
    }

    public async Task<TimeSpan> GetPlayTimeForTrackWithPlaycount(string artistName, string trackName, long playcount, TopTimeListened topTimeListened = null)
    {
        await CacheAllTrackLengths();

        long timeListened = 0;

        if (topTimeListened != null)
        {
            timeListened += topTimeListened.MsPlayed;
            playcount -= topTimeListened.PlaysWithPlayTime;
        }

        var length = GetTrackLengthForTrack(artistName, trackName);

        timeListened += length * playcount;

        return TimeSpan.FromMilliseconds(timeListened);
    }

    private long GetTrackLengthForTrack(string artistName, string trackName)
    {
        var trackLength = (long?)this._cache.Get(CacheKeyForTrack(trackName.ToLower(), artistName.ToLower()));

        if (trackLength.HasValue)
        {
            return trackLength.Value;
        }

        var avgArtistTrackLength = (long?)this._cache.Get(CacheKeyForArtist(artistName.ToLower()));

        return avgArtistTrackLength ?? 210000;
    }

    public async Task<TimeSpan> GetAllTimePlayTimeForAlbum(List<AlbumTrack> albumTracks, List<UserTrack> userTracks, long totalPlaycount, TopTimeListened topTimeListened = null)
    {
        await CacheAllTrackLengths();

        long timeListenedSeconds = 0;
        var playsLeft = totalPlaycount;

        if (topTimeListened != null)
        {
            timeListenedSeconds += (topTimeListened.MsPlayed / 1000);
            playsLeft -= topTimeListened.PlaysWithPlayTime;
        }

        foreach (var track in albumTracks)
        {
            var albumTrackWithPlaycount = userTracks.FirstOrDefault(f =>
                StringExtensions.SanitizeTrackNameForComparison(track.TrackName)
                    .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));

            if (albumTrackWithPlaycount != null)
            {
                var trackPlaycount = albumTrackWithPlaycount.Playcount;

                var countedTrack = topTimeListened?.CountedTracks?.FirstOrDefault(f =>
                    StringExtensions.SanitizeTrackNameForComparison(track.TrackName)
                        .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));

                if (countedTrack != null)
                {
                    trackPlaycount -= countedTrack.CountedPlays;
                }

                if (trackPlaycount > 0)
                {
                    var trackLength = track.DurationSeconds ?? (GetTrackLengthForTrack(track.ArtistName, track.ArtistName) / 1000);

                    timeListenedSeconds += (trackLength * trackPlaycount);
                    playsLeft -= trackPlaycount;
                }
            }
        }

        if (playsLeft > 0)
        {
            var avgTrackLengthSeconds = albumTracks.Average(a => a.DurationSeconds);

            if (avgTrackLengthSeconds == null)
            {
                var avgArtistTrackLength = (long?)this._cache.Get(CacheKeyForArtist(albumTracks.First().ArtistName));
                avgTrackLengthSeconds = (avgArtistTrackLength / 1000) ?? 210;
            }

            timeListenedSeconds += (playsLeft * (long)avgTrackLengthSeconds);
        }

        return TimeSpan.FromSeconds(timeListenedSeconds);
    }

    public async Task<TimeSpan> GetAllTimePlayTimeForArtist(string artistName, List<UserTrack> userTracksForArtist, long totalPlaycount, TopTimeListened topTimeListened = null)
    {
        await CacheAllTrackLengths();

        long timeListenedSeconds = 0;
        var playsLeft = totalPlaycount;

        if (topTimeListened != null)
        {
            timeListenedSeconds += (topTimeListened.MsPlayed / 1000);
            playsLeft -= topTimeListened.PlaysWithPlayTime;
        }

        foreach (var track in userTracksForArtist)
        {
            var trackPlaycount = track.Playcount;

            var countedTrack = topTimeListened?.CountedTracks?.FirstOrDefault(f =>
                StringExtensions.SanitizeTrackNameForComparison(track.Name)
                    .Equals(StringExtensions.SanitizeTrackNameForComparison(f.Name)));

            if (countedTrack != null)
            {
                trackPlaycount -= countedTrack.CountedPlays;
            }

            if (trackPlaycount > 0)
            {
                var trackLength = GetTrackLengthForTrack(track.ArtistName, track.ArtistName) / 1000;

                timeListenedSeconds += (trackLength * trackPlaycount);
                playsLeft -= trackPlaycount;
            }
        }

        if (playsLeft > 0)
        {
            long avgTrackLength;
            if (totalPlaycount > 50)
            {
                avgTrackLength = timeListenedSeconds / (totalPlaycount - playsLeft);
            }
            else
            {
                var avgArtistTrackLength = (long?)this._cache.Get(CacheKeyForArtist(artistName));
                avgTrackLength = (avgArtistTrackLength / 1000) ?? 210;
            }

            timeListenedSeconds += (playsLeft * (long)avgTrackLength);
        }

        return TimeSpan.FromSeconds(timeListenedSeconds);
    }

    private async Task CacheAllTrackLengths()
    {
        const string cacheKey = "track-lengths-cached";
        var cacheTime = TimeSpan.FromMinutes(60);

        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        const string sql = "SELECT LOWER(artist_name) as artist_name, LOWER(name) as track_name, duration_ms " +
                           "FROM public.tracks where duration_ms is not null;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var trackLengths = (await connection.QueryAsync<TrackLengthDto>(sql)).ToList();

        foreach (var length in trackLengths)
        {
            this._cache.Set(CacheKeyForTrack(length.TrackName, length.ArtistName), length.DurationMs, cacheTime);
        }

        foreach (var artistLength in trackLengths.GroupBy(g => g.ArtistName))
        {
            this._cache.Set(CacheKeyForArtist(artistLength.Key), (long)artistLength.Average(a => a.DurationMs), cacheTime);
        }

        this._cache.Set(cacheKey, true, cacheTime);
    }

    private static string CacheKeyForTrack(string trackName, string artistName)
    {
        return $"track-length-{trackName}-{artistName}";
    }
    private static string CacheKeyForArtist(string artistName)
    {
        return $"artist-length-avg-{artistName}";
    }

    public async Task<List<WhoKnowsObjectWithUser>> UserPlaysToGuildLeaderboard(ICommandContext context, List<UserPlay> userPlays, IDictionary<int, FullGuildUser> guildUsers)
    {
        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        var userPlaysPerUser = userPlays
            .GroupBy(g => g.UserId)
            .ToList();

        for (var i = 0; i < userPlaysPerUser.Count(); i++)
        {
            var user = userPlaysPerUser[i];

            var timeListened = await GetPlayTimeForPlays(user);


            if (guildUsers.TryGetValue(user.Key, out var guildUser))
            {
                var userName = guildUser.UserName ?? guildUser.UserNameLastFM;

                if (i <= 10)
                {
                    var discordUser = await context.Guild.GetUserAsync(guildUser.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.DisplayName;
                    }
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Playcount = (int)timeListened.TotalMinutes,
                    LastFMUsername = guildUser.UserNameLastFM,
                    UserId = user.Key,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles,
                    Name = StringExtensions.GetListeningTimeString(timeListened)
                });
            }
        }

        return whoKnowsAlbumList
            .OrderByDescending(o => o.Playcount)
            .ToList();
    }

    public async Task<bool> IsValidScrobble(string artistName, string trackName, int msPlayed)
    {
        if (msPlayed < 30000)
        {
            return false;
        }

        if (msPlayed > 240000)
        {
            return true;
        }

        await CacheAllTrackLengths();

        var trackLength = GetTrackLengthForTrack(artistName, trackName);

        if (msPlayed > trackLength / 2)
        {
            return true;
        }

        return false;
    }
}
