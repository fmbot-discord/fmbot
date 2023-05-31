using System;

namespace FMBot.Domain.Models;

public class FullGuildUser
{
    public int UserId { get; set; }
    public int GuildId { get; set; }
    public string UserName { get; set; }
    public bool? Bot { get; set; }
    public DateTime? LastMessage { get; set; }

    public ulong[] Roles { get; set; }
    public decimal[] DtoRoles { get; set; }

    public ulong DiscordUserId { get; set; }
    public string UserNameLastFM { get; set; }
    public DateTime? LastUsed { get; set; }

    public bool BlockedFromCrowns { get; set; }
    public bool BlockedFromWhoKnows { get; set; }
}
