using System;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class SupporterStripe
{
    public ulong PurchaserDiscordUserId { get; set; }

    public ulong PurchaserLastFmUserName { get; set; }

    public ulong? GiftReceiverDiscordUserId { get; set; }

    public string Email { get; set; }

    public string StripeCustomerId { get; set; }

    public string StripeSubscriptionId { get; set; }

    public DateTime DateStarted { get; set; }

    public DateTime? DateEnding { get; set; }

    public bool EntitlementDeleted { get; set; }

    public int Quantity { get; set; }

    public SupporterStripeType Type { get; set; }
}
