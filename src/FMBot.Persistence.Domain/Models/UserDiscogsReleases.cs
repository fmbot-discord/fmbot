using System;

namespace FMBot.Persistence.Domain.Models;

public class UserDiscogsReleases
{
    public int UserDiscogsReleaseId { get; set; }

    public int UserId { get; set; }

    public int InstanceId { get; set; }

    public DateTime DateAdded { get; set; }
    public int? Rating { get; set; }
    public string Quantity { get; set; }

    public int ReleaseId { get; set; }

    public DiscogsRelease Release { get; set; }
    public User User { get; set; }
}
