using System;

namespace FMBot.Persistence.Domain.Models;

public class InactiveUsers
{
    public int InactiveUserId { get; set; }

    public int? UserId { get; set; }

    public string UserNameLastFM { get; set; }

    public int? RecentTracksPrivateCount { get; set; }

    public int? NoScrobblesErrorCount { get; set; }

    public int? FailureErrorCount { get; set; }
        
    public int? MissingParametersErrorCount { get; set; }

    public DateTime Created { get; set; }

    public DateTime Updated { get; set; }

    public bool? Removed { get; set; }

    public User User { get; set; }
}
