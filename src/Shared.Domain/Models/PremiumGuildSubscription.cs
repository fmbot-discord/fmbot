namespace Shared.Domain.Models;

// This file is managed in Shared.Domain and copied to child projects
public class PremiumGuildSubscription
{
    public int Id { get; set; }

    public ulong DiscordGuildId { get; set; }

    public ulong? PurchaserDiscordUserId { get; set; }

    public string PurchaserLastFmUserName { get; set; }

    public string StripeCustomerId { get; set; }

    public string StripeSubscriptionId { get; set; }

    public ulong? DiscordEntitlementId { get; set; }

    public DateTime DateStarted { get; set; }

    public DateTime? DateEnding { get; set; }

    public bool EntitlementDeleted { get; set; }

    public string Currency { get; set; }

    public string PurchaseSource { get; set; }

    public bool WelcomeMessageSent { get; set; }

    public int? TimesTransferred { get; set; }

    public DateTime? LastTimeTransferred { get; set; }
}
