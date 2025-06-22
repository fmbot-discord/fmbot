using Shared.Domain.Enums;

namespace Shared.Domain.Models;

public class StripePricing
{
    public int Id { get; set; }


    public bool Default { get; set; }

    public string[] Locales { get; set; }

    public StripeSupporterType Type { get; set; }

    public string Currency { get; set; }

    public string MonthlyPriceId { get; set; }

    public string MonthlyPriceString { get; set; }

    public string MonthlySubText { get; set; }

    public string MonthlySummary { get; set; }


    public string QuarterlyPriceId { get; set; }

    public string QuarterlyPriceString { get; set; }

    public string QuarterlySubText { get; set; }

    public string QuarterlySummary { get; set; }


    public string YearlyPriceId { get; set; }

    public string YearlyPriceString { get; set; }

    public string YearlySubText { get; set; }

    public string YearlySummary { get; set; }


    public string TwoYearPriceId { get; set; }

    public string TwoYearPriceString { get; set; }

    public string TwoYearSubText { get; set; }

    public string TwoYearSummary { get; set; }


    public string LifetimePriceId { get; set; }

    public string LifetimePriceString { get; set; }

    public string LifetimeSubText { get; set; }

    public string LifetimeSummary { get; set; }


    public string ByePromo { get; set; }

    public string ByePromoSubText { get; set; }
}
