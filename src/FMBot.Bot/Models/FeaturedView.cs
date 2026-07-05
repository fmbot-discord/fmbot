
using FMBot.Domain.Attributes;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.Models;

public enum FeaturedView
{
    [Option("Global")]
    Global = 1,
    [Option("Members")]
    [SlashCommandChoice(Name = "Members")]
    Server = 2,
    [Option("Friends")]
    Friends = 3,
    [Option("User")]
    User = 4,
    [Option("Server featured")]
    [SlashCommandChoice(Name = "Server featured")]
    GuildFeatured = 5
}
