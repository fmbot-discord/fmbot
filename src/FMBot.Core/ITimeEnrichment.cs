using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core;

public interface ITimeEnrichment
{
    Task<TimeSpan> EnrichPlaysWithPlayTime(ICollection<UserPlay> plays, bool adjustForBans);

    Task<TimeSpan> GetTrackLength(string artistName, string trackName, bool useAverages, bool adjustForBans);

    Task<TimeSpan> GetAverageArtistTrackLength(string artistName);
}
