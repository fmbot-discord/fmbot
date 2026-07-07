using System.Collections.Generic;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Models;

public class AutopostSnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; set; }

    public AutopostType Type { get; set; }

    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }

    public List<AutopostSnapshotSection> Sections { get; set; } = [];
}

public class AutopostSnapshotSection
{
    public AutopostEntityType EntityType { get; set; }

    public string Title { get; set; }

    public List<AutopostSnapshotEntry> Entries { get; set; } = [];
}

public class AutopostSnapshotEntry
{
    public int Rank { get; set; }

    public int? ArtistId { get; set; }

    public int? AlbumId { get; set; }

    public int? TrackId { get; set; }

    public string Name { get; set; }

    public string ArtistName { get; set; }

    public int Playcount { get; set; }

    public int Listeners { get; set; }
}

public enum AutopostEntityType
{
    Artist = 1,
    Album = 2,
    Track = 3,
    NewRelease = 4
}
