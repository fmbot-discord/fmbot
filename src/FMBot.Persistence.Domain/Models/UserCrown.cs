using System;

namespace FMBot.Persistence.Domain.Models;

public class UserCrown
{
    public int CrownId { get; set; }

    public int GuildId { get; set; }

    public Guild Guild { get; set; }

    public int UserId { get; set; }

    public User User { get; set; }

    public string ArtistName { get; set; }

    public int CurrentPlaycount { get; set; }

    public int StartPlaycount { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    public bool Active { get; set; }

    public bool SeededCrown { get; set; }
}
