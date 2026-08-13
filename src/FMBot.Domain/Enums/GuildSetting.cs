using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum GuildSetting
{
    [Option("Prefix", "Prefix for text commands")]
    [SettingSection(GuildSettingSection.General)]
    TextPrefix = 1,
    [Option("Emote reactions", "Emotes that will automatically be added to 'fm' and 'featured'")]
    [SettingSection(GuildSettingSection.General)]
    EmoteReactions = 2,
    [Option("Default 'fm' type", "Default 'fm' embed type for everyone")]
    [SettingSection(GuildSettingSection.General)]
    DefaultEmbedType = 3,
    [Option("Language", "Language for .fmbot responses in this server")]
    [SettingSection(GuildSettingSection.General)]
    Language = 4,

    [Option("Inactive on .fmbot", "Hide members who haven't used .fmbot in a while")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    WhoKnowsActivityThreshold = 10,
    [Option("Blocked members", "See which members are manually blocked from WhoKnows")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    WhoKnowsBlockedUsers = 11,
    [Option("Allowed roles", "Only these roles show up in server-wide charts")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    AllowedRoles = 12,
    [Option("Blocked roles", "These roles are always hidden from server-wide charts")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    BlockedRoles = 13,
    [Option("Bot management roles", "Roles that are allowed to manage .fmbot in this server")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    BotManagementRoles = 14,
    [Option("Inactive in this server", "Hide members who haven't sent a message in a while")]
    [SettingSection(GuildSettingSection.MemberFilters)]
    ServerActivityThreshold = 15,

    [Option("Crown activity threshold", "Filter fmbot-inactive users from earning crowns")]
    [SettingSection(GuildSettingSection.Crowns)]
    CrownActivityThreshold = 20,
    [Option("Crownblocked users", "See which users are manually blocked from earning crowns")]
    [SettingSection(GuildSettingSection.Crowns)]
    CrownBlockedUsers = 21,
    [Option("Crown minimum playcount", "Change the minimum playcount for earning a crown")]
    [SettingSection(GuildSettingSection.Crowns)]
    CrownMinimumPlaycount = 22,
    [Option("Crownseeder", "Automatically generate all crowns for your server")]
    [SettingSection(GuildSettingSection.Crowns)]
    CrownSeeder = 23,
    [Option("Crown functionality", "Completely enable or disable crowns on your server")]
    [SettingSection(GuildSettingSection.Crowns)]
    CrownsDisabled = 24,

    [Option("Disabled channel commands", "Toggle commands or the bot per channel")]
    [SettingSection(GuildSettingSection.Commands)]
    DisabledCommands = 30,

    [Option("Disabled server commands", "Toggle commands server-wide")]
    [SettingSection(GuildSettingSection.Commands)]
    DisabledGuildCommands = 31,

    [Option("Server shortcuts", "Shared text command shortcuts for everyone in this server")]
    [SettingSection(GuildSettingSection.Commands)]
    ServerShortcuts = 32,

    [Option("Server autoposts", "Automatically post recaps and top charts to channels on a schedule")]
    [SettingSection(GuildSettingSection.Automation)]
    ServerAutoposts = 40,

    [Option("Bot branding", "Give .fmbot a custom avatar and look in this server")]
    [SettingSection(GuildSettingSection.Automation)]
    BotBranding = 41,
}
