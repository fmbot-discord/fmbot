using System;

namespace FMBot.Persistence.Domain.Models;

public class GuildUser
{
    public int UserId { get; set; }
    public User User { get; set; }
    public int GuildId { get; set; }
    public Guild Guild { get; set; }
    public string UserName { get; set; }
    public bool? Bot { get; set; }
    public bool? WhoKnowsWhitelisted { get; set; }
    public bool? WhoKnowsBlocked { get; set; }
    public ulong[] Roles { get; set; }

    public DateTime? LastMessage { get; set; }
}
