namespace FMBot.Domain.Models;

public enum SubscriptionType
{
    MonthlyOpenCollective = 1,

    YearlyOpenCollective = 2,

    LifetimeOpenCollective = 3,

    Discord = 4,

    LifetimeStripeManual = 7,
    Stripe = 8,
}
