using System;
using System.Collections.Generic;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class GlobalFilterCandidate
{
    public int UserId { get; set; }

    public ulong DiscordUserId { get; set; }

    public string UserNameLastFm { get; set; }

    public int PlayCount { get; set; }

    public long TotalMsPlayed { get; set; }

    public DateTime FirstPlay { get; set; }

    public DateTime LastPlay { get; set; }
}

public class BottedCheckStats
{
    public int PlaysMonth { get; set; }

    public int PlaysWeek { get; set; }

    public long MsMonth { get; set; }

    public long MsWeek { get; set; }

    public int UnknownDurationPlays { get; set; }

    public int DuplicatePlays { get; set; }

    public int ShortTrackPlays { get; set; }

    public int MaxPlaysInDay { get; set; }

    public DateTime? MaxPlaysDay { get; set; }

    public int DaysOverPlayLimit { get; set; }

    public List<BottedCheckTopTrack> TopTracks { get; set; }

    public BottedCheckTopTrack TopShortTrack { get; set; }

    public string TopArtistName { get; set; }

    public long TopArtistPlaycount { get; set; }
}

public class BottedCheckTopTrack
{
    public string Name { get; set; }

    public string ArtistName { get; set; }

    public long Playcount { get; set; }

    public int? DurationMs { get; set; }
}

public class WhoKnowsSettings
{
    public bool HidePrivateUsers { get; set; }

    public bool ShowBotters { get; set; }

    public bool QualityFilterDisabled { get; set; } = false;

    public bool AdminView { get; set; }

    public string NewSearchValue { get; set; }

    public WhoKnowsResponseMode ResponseMode { get; set; }

    public bool DisplayRoleFilter { get; set; }

    public bool RedirectsEnabled { get; set; } = true;
}
