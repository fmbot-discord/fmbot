
using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models;

public enum GuildViewType
{
    [Option("Overview", localizationKey: "shared.overview")]
    Overview = 1,
    [Option("Ordered by total crowns", localizationKey: "members.viewCrowns")]
    Crowns = 2,
    [Option("Ordered by recent listening time", localizationKey: "members.viewListeningTime")]
    ListeningTime = 3,
    [Option("Ordered by total playcount (scrobbles)", localizationKey: "members.viewPlays")]
    Plays = 4
}

