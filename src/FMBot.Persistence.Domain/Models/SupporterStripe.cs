using System;

namespace FMBot.Persistence.Domain.Models;

public class StripeSupporter
{
    public ulong DiscordUserId { get; set; }

    public string Email { get; set; }

    public string StripeSubscriptionId { get; set; }

    public string StripeCustomerId { get; set; }

    public DateTime DateStarted { get; set; }

    public DateTime? DateEnding { get; set; }

    public bool SubscriptionDeleted { get; set; }
}
