using Shared.Domain.Enums;

namespace Shared.Domain.Models;

// This file is managed in Shared.Domain and copied to child projects
public class StripeSupporter
{
    public int Id { get; set; }

    public ulong PurchaserDiscordUserId { get; set; }

    public string PurchaserLastFmUserName { get; set; }

    public ulong? GiftReceiverDiscordUserId { get; set; }

    public string StripeCustomerId { get; set; }

    public string StripeSubscriptionId { get; set; }

    public DateTime DateStarted { get; set; }

    public DateTime? DateEnding { get; set; }

    public bool EntitlementDeleted { get; set; }

    public int Quantity { get; set; }

    public int? TimesTransferred { get; set; }

    public DateTime? LastTimeTransferred { get; set; }

    public StripeSupporterType Type { get; set; }
}
