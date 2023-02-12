using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum GuildSetting
{
    [Option("Prefix", "Prefix for text commands")]
    TextPrefix = 1,
    [Option("Emote reactions", "Emotes that will automatically be added to 'fm' and 'featured'")]
    EmoteReactions = 2,
    [Option("Default 'fm' type", "Default 'fm' embed type for everyone")]
    DefaultEmbedType = 3,

    [Option("WhoKnows activity threshold", "Filter inactive users from WhoKnows")]
    WhoKnowsActivityThreshold = 10,
    [Option("WhoKnows blocked users", "See which users are manually blocked from WhoKnows")]
    WhoKnowsBlockedUsers = 11,

    [Option("Crown activity threshold", "Filter inactive users from earning crowns")]
    CrownActivityThreshold = 20,
    [Option("WhoKnows blocked users", "See which users are manually blocked from earning crowns")]
    CrownBlockedUsers = 21,
    [Option("Crown minimum playcount", "Change the minimum playcount for earning a crown")]
    CrownMinimumPlaycount = 22,
    [Option("Crown functionality", "Completely enable or disable crowns on your server")]
    CrownsDisabled = 23,

    [Option("Disabled commands", "Enable or disable commands server-wide")]
    DisabledCommands = 30,
}
