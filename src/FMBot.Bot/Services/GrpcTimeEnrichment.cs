using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Core;
using Google.Protobuf.Collections;
using Web.InternalApi;
using UserPlay = FMBot.Persistence.Domain.Models.UserPlay;

namespace FMBot.Bot.Services;

public class GrpcTimeEnrichment : ITimeEnrichment
{
    private readonly TimeEnrichment.TimeEnrichmentClient _timeEnrichment;

    public GrpcTimeEnrichment(TimeEnrichment.TimeEnrichmentClient timeEnrichment)
    {
        this._timeEnrichment = timeEnrichment;
    }

    public async Task<TimeSpan> EnrichPlaysWithPlayTime(ICollection<UserPlay> plays, bool adjustForBans)
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

        return TimeSpan.FromSeconds(reply.TotalPlayTime.Seconds);
    }

    public async Task<TimeSpan> GetTrackLength(string artistName, string trackName, bool useAverages, bool adjustForBans)
    {
        var length = await this._timeEnrichment.GetTrackLengthAsync(new TrackLengthRequest
        {
            ArtistName = artistName,
            TrackName = trackName,
            UseAverages = useAverages,
            AdjustForBans = adjustForBans
        });

        return length.TrackLength.ToTimeSpan();
    }

    public async Task<TimeSpan> GetAverageArtistTrackLength(string artistName)
    {
        var avgArtistTrackLength = await this._timeEnrichment.GetAverageArtistTrackLengthAsync(
            new AverageArtistTrackLengthRequest
            {
                ArtistName = artistName
            });

        return TimeSpan.FromSeconds(avgArtistTrackLength.AvgLength.Seconds);
    }
}
