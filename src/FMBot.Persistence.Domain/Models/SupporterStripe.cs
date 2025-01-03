using System;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class SupporterStripe
{
    public ulong PurchaserDiscordUserId { get; set; }

    public ulong PurchaserLastFmUserName { get; set; }

    public ulong ReceiverDiscordUserId { get; set; }

    public string Email { get; set; }

    public string StripeCustomerId { get; set; }

    public string StripeSubscriptionId { get; set; }

    public string Type { get; set; }

    public DateTime DateStarted { get; set; }

    public DateTime? DateEnding { get; set; }

    public bool EntitlementDeleted { get; set; }
}
