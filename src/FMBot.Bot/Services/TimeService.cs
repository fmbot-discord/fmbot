using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Web.InternalApi;
using UserPlay = FMBot.Persistence.Domain.Models.UserPlay;

namespace FMBot.Bot.Services;

public class TimeService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly TimeEnrichment.TimeEnrichmentClient _timeEnrichment;

    public TimeService(IMemoryCache cache, IOptions<BotSettings> botSettings, TimeEnrichment.TimeEnrichmentClient timeEnrichment)
    {
        this._cache = cache;
        this._timeEnrichment = timeEnrichment;
        this._botSettings = botSettings.Value;
    }

    public static TimeSpan GetPlayTimeForEnrichedPlays(IEnumerable<UserPlay> plays, bool adjustForBans = false)
    {
        return TimeSpan.FromMilliseconds(plays.Sum(s => s.MsPlayed.GetValueOrDefault()));
    }

    public async Task<(ICollection<UserPlay> enrichedPlays, TimeSpan totalPlayTime)> EnrichPlaysWithPlayTime(ICollection<UserPlay> plays, bool adjustForBans = false)
    {
        var simplePlays = plays.Select(s => new SimpleUserPlay
        {
            UserPlayId = s.UserPlayId,
            ArtistName = s.ArtistName,
            MsPlayed = s.MsPlayed.GetValueOrDefault(),
            TrackName = s.TrackName
        });

        var repeatedField = new RepeatedField<SimpleUserPlay>();
        repeatedField.AddRange(simplePlays);

        var userPlayList = new UserPlayList
        {
            UserPlays = { repeatedField },
            AdjustForBans = adjustForBans
        };

        var reply = await this._timeEnrichment.ProcessUserPlaysAsync(userPlayList);

        var enrichedPlays = reply.UserPlays.ToDictionary(d => d.UserPlayId);
        foreach (var play in plays)
        {
            if (enrichedPlays.TryGetValue(play.UserPlayId, out var enrichedPlay))
            {
                play.MsPlayed = enrichedPlay.MsPlayed;
            }
        }

        var totalPlayTime = TimeSpan.FromSeconds(reply.TotalPlayTime.Seconds);
        return (plays, totalPlayTime);
    }

    public async Task<TimeSpan> GetPlayTimeForTrackWithPlaycount(string artistName, string trackName, long playcount, TopTimeListened topTimeListened = null)
    {
        long timeListened = 0;

        if (topTimeListened != null)
        {
            timeListened += topTimeListened.MsPlayed;
            playcount -= topTimeListened.PlaysWithPlayTime;
        }

        var length = await this._timeEnrichment.GetTrackLengthAsync(new TrackLengthRequest
        {
            ArtistName = artistName,
            TrackName = trackName,
            UseAverages = true
        });

        timeListened += (long)length.TrackLength.ToTimeSpan().TotalMilliseconds * playcount;

        return TimeSpan.FromMilliseconds(timeListened);
    }

    public async Task<TimeSpan> GetTrackLengthForTrack(string artistName, string trackName, bool adjustForBans = false)
    {
        var length = await this._timeEnrichment.GetTrackLengthAsync(new TrackLengthRequest
        {
            ArtistName = artistName,
            TrackName = trackName,
            UseAverages = true,
            AdjustForBans = adjustForBans
        });

        return length.TrackLength.ToTimeSpan();
    }

    public async Task<TimeSpan> GetAllTimePlayTimeForAlbum(List<AlbumTrack> albumTracks, List<UserTrack> userTracks, long totalPlaycount, TopTimeListened topTimeListened = null)
    {
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
                    var trackLength = track.DurationSeconds ?? (int)(await GetTrackLengthForTrack(track.ArtistName, track.ArtistName)).TotalSeconds;

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
                var avgArtistTrackLength = await this._timeEnrichment.GetAverageArtistTrackLengthAsync(
                    new AverageArtistTrackLengthRequest
                    {
                        ArtistName = albumTracks.First().ArtistName
                    });

                avgTrackLengthSeconds = avgArtistTrackLength.AvgLength.Seconds != 0 ? avgArtistTrackLength.AvgLength.Seconds : 210;
            }

            timeListenedSeconds += (playsLeft * (long)avgTrackLengthSeconds);
        }

        return TimeSpan.FromSeconds(timeListenedSeconds);
    }

    public async Task<List<WhoKnowsObjectWithUser>> UserPlaysToGuildLeaderboard(IGuild discordGuild, List<UserPlay> userPlays, IDictionary<int, FullGuildUser> guildUsers)
    {
        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        var userPlaysPerUser = userPlays
            .GroupBy(g => g.UserId)
            .ToList();

        foreach (var user in userPlaysPerUser)
        {
            var timeListened = await EnrichPlaysWithPlayTime(user.ToList());

            if (guildUsers.TryGetValue(user.Key, out var guildUser))
            {
                var userName = guildUser.UserName ?? guildUser.UserNameLastFM;

                var discordUser = await discordGuild.GetUserAsync(guildUser.DiscordUserId, CacheMode.CacheOnly);
                if (discordUser != null)
                {
                    userName = discordUser.DisplayName;
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Playcount = (int)timeListened.totalPlayTime.TotalMinutes,
                    LastFMUsername = guildUser.UserNameLastFM,
                    UserId = user.Key,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles,
                    Name = StringExtensions.GetListeningTimeString(timeListened.totalPlayTime)
                });
            }
        }

        return whoKnowsAlbumList
            .OrderByDescending(o => o.Playcount)
            .ToList();
    }
}
