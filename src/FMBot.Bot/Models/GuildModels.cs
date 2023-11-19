using Discord.Interactions;

namespace FMBot.Bot.Models;

public enum GuildViewType
{
    [ChoiceDisplay("Overview")]
    Overview = 1,
    [ChoiceDisplay("Ordered by total crowns")]
    Crowns = 2,
    [ChoiceDisplay("Ordered by recent listening time")]
    ListeningTime = 3,
    [ChoiceDisplay("Ordered by total playcount (scrobbles)")]
    Plays = 4
}

