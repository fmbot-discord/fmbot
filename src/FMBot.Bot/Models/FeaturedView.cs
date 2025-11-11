
using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models;

public enum FeaturedView
{
    [Option("Global")]
    Global = 1,
    [Option("Server")]
    Server = 2,
    [Option("Friends")]
    Friends = 3,
    [Option("User")]
    User = 4
}
