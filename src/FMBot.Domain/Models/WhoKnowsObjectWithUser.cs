using System;

namespace FMBot.Domain.Models;


public class WhoKnowsObjectWithUser
{
    public string Name { get; set; }

    public int Playcount { get; set; }

    public string LastFMUsername { get; set; }

    public string DiscordName { get; set; }

    public int UserId { get; set; }

    public DateTime? RegisteredLastFm { get; set; }

    public PrivacyLevel? PrivacyLevel { get; set; }

    public ulong[] Roles { get; set; }
    public DateTime? LastUsed { get; set; }
    public DateTime? LastMessage { get; set; }

    public bool? SameServer { get; set; }
}
