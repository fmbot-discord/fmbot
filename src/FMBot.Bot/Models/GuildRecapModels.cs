namespace FMBot.Bot.Models;

public class GuildPlayStats
{
    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public enum AutopostPostResult
{
    Posted = 1,
    NoData = 3,
    Failed = 4
}
