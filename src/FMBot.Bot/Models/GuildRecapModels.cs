namespace FMBot.Bot.Models;

public class GuildPlayStats
{
    public int TotalPlaycount { get; set; }

    public int ListenerCount { get; set; }
}

public enum GuildRecapPostResult
{
    Posted = 1,
    NoChannel = 2,
    NoData = 3,
    Failed = 4
}
