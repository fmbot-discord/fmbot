using System;
using System.Globalization;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Extensions;

public static class FormatExtension
{
    public static string Format(this int? number, NumberFormat format)
    {
        return !number.HasValue ? string.Empty : number.Value.Format(format);
    }

    public static string Format(this int number, NumberFormat format)
    {
        var culture = GetCulture(format);
        var numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (format == NumberFormat.NoSeparator)
        {
            numberFormat.NumberGroupSeparator = "";
            numberFormat.NumberDecimalSeparator = ",";
        }

        return number.ToString("N0", numberFormat);
    }

    public static string Format(this long? number, NumberFormat format)
    {
        return !number.HasValue ? string.Empty : number.Value.Format(format);
    }

    public static string Format(this long number, NumberFormat format)
    {
        var culture = GetCulture(format);
        var numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (format == NumberFormat.NoSeparator)
        {
            numberFormat.NumberGroupSeparator = "";
            numberFormat.NumberDecimalSeparator = ",";
        }

        return number.ToString("N0", numberFormat);
    }

    public static string Format(this decimal? number, NumberFormat format)
    {
        return !number.HasValue ? string.Empty : number.Value.Format(format);
    }

    public static string Format(this decimal number, NumberFormat format)
    {
        var culture = GetCulture(format);
        var numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (format == NumberFormat.NoSeparator)
        {
            numberFormat.NumberGroupSeparator = "";
            numberFormat.NumberDecimalSeparator = ",";
        }

        if (number == Math.Truncate(number))
        {
            return number.ToString("N0", numberFormat);
        }

        return number.ToString("N2", numberFormat);
    }

    public static string FormatPercentage(this decimal number, NumberFormat format)
    {
        var culture = GetCulture(format);
        var numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (format == NumberFormat.NoSeparator)
        {
            numberFormat.NumberGroupSeparator = "";
            numberFormat.NumberDecimalSeparator = ",";
        }

        return number.ToString("P", numberFormat);
    }

    public static string Format(this double? number, NumberFormat format)
    {
        return !number.HasValue ? string.Empty : number.Value.Format(format);
    }

    public static string Format(this double number, NumberFormat format)
    {
        var culture = GetCulture(format);
        var numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
        if (format == NumberFormat.NoSeparator)
        {
            numberFormat.NumberGroupSeparator = "";
            numberFormat.NumberDecimalSeparator = ",";
        }

        if (Math.Abs(number - Math.Truncate(number)) < 1e-10)
        {
            return number.ToString("N0", numberFormat);
        }

        return number.ToString("N1", numberFormat);
    }

    private static CultureInfo GetCulture(NumberFormat format)
    {
        return format switch
        {
            NumberFormat.DecimalSeparator => new CultureInfo("de-DE"),
            NumberFormat.CommaSeparator => new CultureInfo("en-US"),
            NumberFormat.SpaceSeparator => new CultureInfo("fr-FR"),
            NumberFormat.NoSeparator => CultureInfo.InvariantCulture,
            _ => CultureInfo.InvariantCulture
        };
    }
}
