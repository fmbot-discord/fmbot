using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

[Flags]
public enum GuildFlags : long
{
    StaffCommandsAvailable = 1 << 1,
    PremiumServerTester = 1 << 2,
    LegacyWhoKnowsWhitelist = 1 << 3,
}
