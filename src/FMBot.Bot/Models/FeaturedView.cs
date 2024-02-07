using Discord.Interactions;

namespace FMBot.Bot.Models;

public enum FeaturedView
{
    [ChoiceDisplay("Global")]
    Global = 1,
    [ChoiceDisplay("Server")]
    Server = 2,
    [ChoiceDisplay("Friends")]
    Friends = 3,
    [ChoiceDisplay("User")]
    User = 4
}
