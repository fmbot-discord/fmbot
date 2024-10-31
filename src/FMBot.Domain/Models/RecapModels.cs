using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class BotUsageStats
{
    public int TotalCommands { get; set; }
    public Dictionary<string, int> CommandUsage { get; set; }
    public int UniqueArtistsSearched { get; set; }
    public int UniqueAlbumsSearched { get; set; }
    public int UniqueTracksSearched { get; set; }
    public Dictionary<string, int> TopSearchedArtists { get; set; }
    public int ServersUsedIn { get; set; }
    public Dictionary<int, int> ActivityByHour { get; set; }
    public decimal ErrorRate { get; set; }
    public int TotalGuilds { get; set; }

    // Time-based metrics
    public DateTime MostActiveDay { get; set; }
    public int MostActiveHour { get; set; }
    public DayOfWeek MostActiveDayOfWeek { get; set; }

    public Dictionary<string, int> TopResponseTypes { get; set; }
    public Dictionary<TimeSpan, int> SessionAnalysis { get; set; }
    public int TotalUniqueSearches { get; set; }
    public float AverageCommandsPerSession { get; set; }
    public Dictionary<DayOfWeek, int> ActivityByDayOfWeek { get; set; }
    public Dictionary<string, List<string>> CommandCombinations { get; set; }
    public TimeSpan AverageTimeBetweenCommands { get; set; }
    public int LongestCommandStreak { get; set; }
    public Dictionary<string, float> CommandSuccessRates { get; set; }
    public List<(DateTime Date, int Count)> PeakUsageDays { get; set; }
}
