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

public class CrownViewSettings
{
    public CrownOrderType CrownOrderType { get; set; }
}

public enum CrownOrderType
{
    Playcount = 1,
    Recent = 2
}
