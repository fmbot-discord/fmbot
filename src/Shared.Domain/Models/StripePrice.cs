using Shared.Domain.Enums;

namespace Shared.Domain.Models;

public class StripePrice
{
    public int Id { get; set; }

    public bool Default { get; set; }

    public string MonthlyPriceId { get; set; }

    public string MonthlyPriceString { get; set; }

    public string MonthlyTagLine { get; set; }

    public string YearlyPriceId { get; set; }

    public string YearlyPriceString { get; set; }

    public string YearlyTagLine { get; set; }

    public string[] Locales { get; set; }

    public StripeSupporterType Type { get; set; }
}
