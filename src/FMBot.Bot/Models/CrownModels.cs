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
    Playcount = 1,
    Recent = 2,
    Stolen = 3
}
