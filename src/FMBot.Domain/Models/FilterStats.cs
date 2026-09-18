using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class FilterStats
{
    public int StartCount { get; set; }

    public List<ulong> Roles { get; set; }

    public bool? RequesterFiltered { get; set; }

    public int? ActivityThresholdFiltered { get; set; }
    public int? GuildActivityThresholdFiltered { get; set; }
    public int? BlockedFiltered { get; set; }
    public int? AllowedRolesFiltered { get; set; }
    public int? BlockedRolesFiltered { get; set; }
    public int? ManualRoleFilter { get; set; }

    public int EndCount { get; set; }
}
