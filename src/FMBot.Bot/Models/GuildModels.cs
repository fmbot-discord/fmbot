
namespace FMBot.Bot.Models;

public enum GuildViewType
{
    [Option("Overview")]
    Overview = 1,
    [Option("Ordered by total crowns")]
    Crowns = 2,
    [Option("Ordered by recent listening time")]
    ListeningTime = 3,
    [Option("Ordered by total playcount (scrobbles)")]
    Plays = 4
}

