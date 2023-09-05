using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class GlobalFilteredUser
{
    public int GlobalFilteredUserId { get; set; }

    public string UserNameLastFm { get; set; }
    public DateTime? RegisteredLastFm { get; set; }
    public int? UserId { get; set; }

    public GlobalFilterReason Reason { get; set; }

    public int? ReasonAmount { get; set; }

    public DateTime? OccurrenceStart { get; set; }
    public DateTime? OccurrenceEnd { get; set; }

    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}
