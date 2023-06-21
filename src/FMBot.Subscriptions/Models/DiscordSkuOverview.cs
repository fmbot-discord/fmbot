namespace FMBot.Subscriptions.Models;

public class DiscordSkuOverview
{
    public List<DiscordEntitlement> Entitlements { get; set; }
}

public class DiscordEntitlement
{
    public ulong DiscordUserId { get; set; }

    public bool Active { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}
