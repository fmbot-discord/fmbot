using System;

namespace FMBot.Persistence.Domain.Models;

public class ListeningPartySubmission
{
    public int Id { get; set; }

    public int ListeningPartyId { get; set; }
    public int UserId { get; set; }

    public int TrackId { get; set; }

    public bool CustomSubmission { get; set; }

    public int? DurationMs { get; set; }

    public DateTime SubmittedDate { get; set; }
    public DateTime? StartedPlayingDate { get; set; }

    public ListeningParty ListeningParty { get; set; }
    public User User { get; set; }
    public Track Track { get; set; }
}
