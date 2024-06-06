using System;

namespace FMBot.Persistence.Domain.Models;

public class JumbleSessionAnswer
{
    public int JumbleSessionAnswerId { get; set; }

    public int JumbleSessionId { get; set; }
    
    public DateTime DateAnswered { get; set; }

    public ulong DiscordUserId { get; set; }
    public bool Correct { get; set; }
    public string Answer { get; set; }

    public JumbleSession JumbleSession { get; set; }
}
