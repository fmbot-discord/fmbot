namespace FMBot.Persistence.Domain.Models;

public class GuildBlockedUser
{
    public int GuildId { get; set; }

    public Guild Guild { get; set; }

    public int UserId { get; set; }

    public User User { get; set; }

    public bool BlockedFromCrowns { get; set; }

    public bool BlockedFromWhoKnows { get; set; }

    public bool SelfBlockFromWhoKnows { get; set; }
}
