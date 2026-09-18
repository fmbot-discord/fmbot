namespace FMBot.Core;

public interface IPremiumGuildLookup
{
    bool IsPremium(ulong discordGuildId);
}
