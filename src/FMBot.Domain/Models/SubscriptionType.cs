namespace FMBot.Domain.Models;

public enum SubscriptionType
{
    MonthlyOpenCollective = 1,

    YearlyOpenCollective = 2,

    LifetimeOpenCollective = 3,

    Discord = 4,

    MonthlyStripe = 5,

    YearlyStripe = 6,

    LifetimeStripe = 7,
}
