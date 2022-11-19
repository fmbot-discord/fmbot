using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class DiscogsRelease
{
    public int DiscogsId { get; set; }
    public int MasterId { get; set; }

    public string CoverUrl { get; set; }
    public int? Year { get; set; }

    public string Format { get; set; }
    public string FormatText { get; set; }
    public List<DiscogsFormatDescriptions> FormatDescriptions { get; set; }

    public string Label { get; set; }
    public string SecondLabel { get; set; }

    public decimal? LowestPrice { get; set; }

    public DiscogsMaster DiscogsMaster { get; set; }

    public List<UserDiscogsReleases> UserDiscogsReleases { get; set; }
}
