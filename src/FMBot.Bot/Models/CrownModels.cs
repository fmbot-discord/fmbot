
using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models;

public class CrownSeedDto
{
    public int UserId { get; set; }

    public string Name { get; set; }

    public int Playcount { get; set; }
}

public class CurrentCrownHolderDto
{
    public int UserId { get; set; }
    public int CurrentPlaycount { get; set; }
    public string UserName { get; set; }
}

public enum CrownViewType
{
    [Option("Active crowns ordered by playcount")]
    Playcount = 1,
    [Option("Recently obtained crowns")]
    Recent = 2,
    [Option("Recently stolen crowns")]
    Stolen = 3
}
