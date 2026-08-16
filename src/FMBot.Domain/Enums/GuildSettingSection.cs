using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum GuildSettingSection
{
    [Option("General", "Prefix, language, default 'fm' type and emote reactions")]
    General = 1,

    [Option("Member filters", "Who appears in WhoKnows and server-wide charts")]
    MemberFilters = 2,

    [Option("Crowns", "Crown functionality, thresholds and the crownseeder")]
    Crowns = 3,

    [Option("Commands", "Toggle commands per server or channel, cooldowns and shortcuts")]
    Commands = 4,

    [Option("Automation", "Autoposts, bot branding and server featured")]
    Automation = 5
}
