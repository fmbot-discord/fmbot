using System;
using System.Collections.Generic;
using FMBot.Domain.Flags;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class GuildRankingSettings
{
    public OrderType OrderType { get; set; }

    public TimePeriod ChartTimePeriod { get; set; }

    public string TimeDescription { get; set; }

    public TimeSettingsModel TimeSettings { get; set; }

    public DateTime StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }

    public string BillboardTimeDescription { get; set; }
    public DateTime BillboardStartDateTime { get; set; }
    public DateTime? BillboardEndDateTime { get; set; }

    public int AmountOfDays { get; set; }
    public int AmountOfDaysWithBillboard { get; set; }

    public string NewSearchValue { get; set; }

    public bool DisplayRoleFilter { get; set; }
}

public class WhoKnowsGlobalArtistDto
{
    public int UserId { get; set; }

    public int Playcount { get; set; }

    public string UserNameLastFm { get; set; }

    public ulong DiscordUserId { get; set; }

    public DateTime? RegisteredLastFm { get; set; }

    public PrivacyLevel PrivacyLevel { get; set; }
}

public class ArtistSearch
{
    public ArtistSearch(ArtistInfo artist, ResponseModel response, int? randomArtistPosition = null, long? randomArtistPlaycount = null, RecentTrack latestScrobble = null)
    {
        this.Artist = artist;
        this.Response = response;
        this.IsRandom = randomArtistPosition.HasValue && randomArtistPlaycount.HasValue;
        this.RandomArtistPosition = randomArtistPosition + 1;
        this.RandomArtistPlaycount = randomArtistPlaycount;
        this.LatestScrobble = latestScrobble;
    }

    public ArtistInfo Artist { get; set; }
    public ResponseModel Response { get; set; }

    public bool IsRandom { get; set; }
    public int? RandomArtistPosition { get; set; }
    public long? RandomArtistPlaycount { get; set; }
    public RecentTrack LatestScrobble { get; set; }
}

public class ArtistImageRow
{
    public string Name { get; set; }
    public string ImageUrl { get; set; }
}

