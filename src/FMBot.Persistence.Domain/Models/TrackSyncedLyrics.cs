using System;

namespace FMBot.Persistence.Domain.Models;

public class TrackSyncedLyrics
{
    public long Id { get; set; }

    public int TrackId { get; set; }

    public TimeSpan Timestamp { get; set; }

    public string Text { get; set; }

    public Track Track { get; set; }
}
