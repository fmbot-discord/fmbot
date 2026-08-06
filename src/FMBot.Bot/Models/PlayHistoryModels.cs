using System;
using System.Collections.Generic;

namespace FMBot.Bot.Models;

public record DayPlayCount(DateTime Day, int Plays);

public class UserPlayHistory
{
    public IReadOnlyList<DayPlayCount> DailyPlays { get; init; } = [];

    public bool HasImported { get; init; }
}

public class PlayHistorySummary
{
    public IReadOnlyList<DayPlayCount> DailyPlays { get; init; } = [];

    public DateTime? FirstPlay { get; init; }

    public DateTime? LastPlay { get; init; }

    public int WeekPlays { get; init; }

    public int MonthPlays { get; init; }
}
