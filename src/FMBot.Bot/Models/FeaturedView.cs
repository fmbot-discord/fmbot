
using FMBot.Domain.Attributes;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.Models;

public enum FeaturedView
{
    [Option("Global", "Global featured", localizationKey: "featured.log.viewGlobal")]
    Global = 1,
    [Option("Members", "Global featured", localizationKey: "featured.log.viewMembers")]
    [SlashCommandChoice(Name = "Members")]
    Server = 2,
    [Option("Friends", "Global featured", localizationKey: "featured.log.viewFriends")]
    Friends = 3,
    [Option("User", "Global featured", localizationKey: "featured.log.viewUser")]
    User = 4,
    [Option("Server", "Server featured", localizationKey: "featured.log.viewServer")]
    [SlashCommandChoice(Name = "Server featured")]
    GuildFeatured = 5,
    [Option("User", "Server featured", localizationKey: "featured.log.viewUser")]
    [SlashCommandChoice(Name = "Server featured user")]
    GuildFeaturedUser = 6
}
