using System;
using System.Collections.Generic;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class Guild
{
    public int GuildId { get; set; }

    public ulong DiscordGuildId { get; set; }

    public string Name { get; set; }

    public string Prefix { get; set; }

    public FmEmbedType? FmEmbedType { get; set; }

    public string[] EmoteReactions { get; set; }

    public string[] DisabledCommands { get; set; }

    public DateTime? LastIndexed { get; set; }

    public bool? SpecialGuild { get; set; }

    public bool? DisableSupporterMessages { get; set; }

    public int? ActivityThresholdDays { get; set; }

    public int? CrownsActivityThresholdDays { get; set; }

    public int? CrownsMinimumPlaycountThreshold { get; set; }

    public bool? CrownsDisabled { get; set; }

    public ulong[] AllowedRoles { get; set; }
    public ulong[] BlockedRoles { get; set; }
    public ulong[] BotManagementRoles { get; set; }
    public ulong[] FilterRoles { get; set; }

    public int? UserActivityThresholdDays { get; set; }

    public string CustomLogo { get; set; }

    public DateTime? LastCrownSeed { get; set; }
    public AutomaticCrownSeeder? AutomaticCrownSeeder { get; set; }

    public GuildFlags? GuildFlags { get; set; }

    public ICollection<GuildUser> GuildUsers { get; set; }

    public ICollection<GuildBlockedUser> GuildBlockedUsers { get; set; }

    public ICollection<UserCrown> GuildCrowns { get; set; }

    public ICollection<Channel> Channels { get; set; }

    public ICollection<Webhook> Webhooks { get; set; }

    public ulong? WhoKnowsWhitelistRoleId { get; set; }
}
