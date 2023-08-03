using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum GuildSetting
{
    [Option("Prefix", "Prefix for text commands")]
    TextPrefix = 1,
    [Option("Emote reactions", "Emotes that will automatically be added to 'fm' and 'featured'")]
    EmoteReactions = 2,
    [Option("Default 'fm' type", "Default 'fm' embed type for everyone")]
    DefaultEmbedType = 3,

    [Option("WhoKnows activity threshold", "Filter fmbot-inactive users from WhoKnows")]
    WhoKnowsActivityThreshold = 10,
    [Option("WhoKnows blocked users", "See which users are manually blocked from WhoKnows")]
    WhoKnowsBlockedUsers = 11,

    [Option("Crown activity threshold", "Filter fmbot-inactive users from earning crowns")]
    CrownActivityThreshold = 20,
    [Option("Crownblocked users", "See which users are manually blocked from earning crowns")]
    CrownBlockedUsers = 21,
    [Option("Crown minimum playcount", "Change the minimum playcount for earning a crown")]
    CrownMinimumPlaycount = 22,
    [Option("Crownseeder", "Automatically generate all crowns for your server")]
    CrownSeeder = 23,
    //[Option("Crown functionality", "Completely enable or disable crowns on your server")]
    //CrownsDisabled = 23,

    [Option("Disabled channel commands", "Toggle commands or the bot per channel")]
    DisabledCommands = 30,

    [Option("Disabled server commands", "Toggle commands server-wide")]
    DisabledGuildCommands = 31,
}
