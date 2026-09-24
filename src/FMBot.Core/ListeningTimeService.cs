using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core;

public class ListeningTimeService
{
    private readonly ITimeEnrichment _timeEnrichment;

    public ListeningTimeService(ITimeEnrichment timeEnrichment)
    {
        this._timeEnrichment = timeEnrichment;
    }

    public static TimeSpan GetPlayTimeForEnrichedPlays(IEnumerable<UserPlay> plays, bool adjustForBans = false)
    {
        return TimeSpan.FromMilliseconds(plays.Sum(s => s.MsPlayed.GetValueOrDefault()));
    }

    public async Task<(ICollection<UserPlay> enrichedPlays, TimeSpan totalPlayTime)> EnrichPlaysWithPlayTime(ICollection<UserPlay> plays, bool adjustForBans = false)
    {
        var totalPlayTime = await this._timeEnrichment.EnrichPlaysWithPlayTime(plays, adjustForBans);
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

        var length = await this._timeEnrichment.GetTrackLength(artistName, trackName, true, false);

        timeListened += (long)length.TotalMilliseconds * playcount;

        return TimeSpan.FromMilliseconds(timeListened);
    }

    public async Task<TimeSpan> GetTrackLengthForTrack(string artistName, string trackName, bool adjustForBans = false)
    {
        return await this._timeEnrichment.GetTrackLength(artistName, trackName, true, adjustForBans);
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
                TrackNameExtensions.SanitizeTrackNameForComparison(track.TrackName)
                    .Equals(TrackNameExtensions.SanitizeTrackNameForComparison(f.Name)));

            if (albumTrackWithPlaycount != null)
            {
                var trackPlaycount = albumTrackWithPlaycount.Playcount;

                var countedTrack = topTimeListened?.CountedTracks?.FirstOrDefault(f =>
                    TrackNameExtensions.SanitizeTrackNameForComparison(track.TrackName)
                        .Equals(TrackNameExtensions.SanitizeTrackNameForComparison(f.Name)));

                if (countedTrack != null)
                {
                    trackPlaycount -= countedTrack.CountedPlays;
                }

                if (trackPlaycount > 0)
                {
                    var trackLength = track.DurationSeconds ?? (int)(await GetTrackLengthForTrack(track.ArtistName, track.TrackName)).TotalSeconds;

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
                var avgArtistTrackLength = await this._timeEnrichment.GetAverageArtistTrackLength(albumTracks.First().ArtistName);

                avgTrackLengthSeconds = avgArtistTrackLength.TotalSeconds != 0 ? avgArtistTrackLength.TotalSeconds : 210;
            }

            timeListenedSeconds += (playsLeft * (long)avgTrackLengthSeconds);
        }

        return TimeSpan.FromSeconds(timeListenedSeconds);
    }
}
