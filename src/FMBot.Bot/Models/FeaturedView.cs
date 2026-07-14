
using FMBot.Domain.Attributes;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.Models;

public enum FeaturedView
{
    [Option("Global", "Global featured")]
    Global = 1,
    [Option("Members", "Global featured")]
    [SlashCommandChoice(Name = "Members")]
    Server = 2,
    [Option("Friends", "Global featured")]
    Friends = 3,
    [Option("User", "Global featured")]
    User = 4,
    [Option("Server", "Server featured")]
    [SlashCommandChoice(Name = "Server featured")]
    GuildFeatured = 5,
    [Option("User", "Server featured")]
    [SlashCommandChoice(Name = "Server featured user")]
    GuildFeaturedUser = 6
}
