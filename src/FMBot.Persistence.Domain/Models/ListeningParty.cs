using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class ListeningParty
{
    public int Id { get; set; }

    public int HostUserId { get; set; }

    public DateTime Created { get; set; }
    public DateTime? PlannedStartDate { get; set; }
    public DateTime? ActualStartDate { get; set; }

    public User HostUser { get; set; }

    public List<ListeningPartySubmission> Submissions { get; set; }
}
