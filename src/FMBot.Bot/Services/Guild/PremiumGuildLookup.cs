using FMBot.Core;
using FMBot.Domain;

namespace FMBot.Bot.Services.Guild;

public class PremiumGuildLookup : IPremiumGuildLookup
{
    public bool IsPremium(ulong discordGuildId)
    {
        return IsPremiumGuild(discordGuildId);
    }

    public static bool IsPremiumGuild(ulong discordGuildId)
    {
        return PublicProperties.PremiumServers.ContainsKey(discordGuildId);
    }
}
