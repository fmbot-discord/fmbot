using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Core;

public static class TimePeriodParser
{
    private static bool TryResolveTimeZone(string timeZone, out TimeZoneInfo timeZoneInfo)
    {
        timeZoneInfo = null;
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return false;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out timeZoneInfo))
        {
            return true;
        }

        timeZoneInfo = TimeZoneInfo.GetSystemTimeZones()
            .FirstOrDefault(w => string.Equals(w.Id, timeZone, StringComparison.OrdinalIgnoreCase));
        return timeZoneInfo != null;
    }

    public static TimeZoneInfo ResolveTimeZone(string timeZone, TimeZoneInfo fallback = null)
    {
        return TryResolveTimeZone(timeZone, out var timeZoneInfo) ? timeZoneInfo : fallback ?? TimeZoneInfo.Utc;
    }

    public static TimeSettingsModel GetTimePeriod(string options,
        TimePeriod defaultTimePeriod = TimePeriod.Weekly,
        DateTime? registeredLastFm = null,
        bool cachedOnly = false,
        bool dailyTimePeriods = true,
        string timeZone = null,
        Language language = Language.English)
    {
        var settingsModel = new TimeSettingsModel();
        bool? customTimePeriod = null;

        options ??= "";
        settingsModel.NewSearchValue = options;
        settingsModel.UsePlays = false;
        settingsModel.UseCustomTimePeriod = false;
        settingsModel.EndDateTime = DateTime.UtcNow;
        settingsModel.DefaultPicked = false;

        var timeZoneInfo = ResolveTimeZone(timeZone);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), timeZoneInfo);
        var localMidnightInUtc = TimeZoneInfo.ConvertTimeToUtc(localTime.Date, timeZoneInfo);

        var year = GetYear(options, false, 1970);
        var month = GetMonth(options, language);

        if (year != null || month != null)
        {
            var startUnspecified = new DateTime(
                year.GetValueOrDefault(DateTime.UtcNow.Year),
                month?.monthNumber ?? 1,
                1);

            settingsModel.StartDateTime = TimeZoneInfo.ConvertTimeToUtc(startUnspecified.Date, timeZoneInfo);

            if (month.HasValue && month.Value.monthNumber > localTime.Month && !year.HasValue)
            {
                settingsModel.StartDateTime = settingsModel.StartDateTime.Value.AddYears(-1);
                startUnspecified = startUnspecified.AddYears(-1);
                year = settingsModel.StartDateTime.Value.Year;
            }

            if (year.HasValue && !month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, [year.Value.ToString()]);
                settingsModel.Description = $"{year}";
                settingsModel.AltDescription = $"year {year}";
                settingsModel.BillboardTimeDescription = $"{year - 1}";
                settingsModel.EndDateTime = settingsModel.StartDateTime.Value.AddYears(1).AddSeconds(-1);
            }

            if (!year.HasValue && month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue,
                    [month.Value.monthName, DateTimeFormatInfo.CurrentInfo.GetMonthName(month.Value.monthNumber)]);

                settingsModel.Description = startUnspecified.ToString("MMMM");
                settingsModel.PeriodMonthDate = startUnspecified;
                settingsModel.AltDescription = $"month {startUnspecified.ToString("MMMM")}";
                settingsModel.EndDateTime = settingsModel.StartDateTime.Value.AddMonths(1).AddSeconds(-1);
                settingsModel.BillboardTimeDescription = $"{startUnspecified.AddMonths(-1):MMMM}";
            }

            if (year.HasValue && month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue,
                    [year.Value.ToString(), month.Value.monthName, DateTimeFormatInfo.CurrentInfo.GetMonthName(month.Value.monthNumber)]);
                ;

                settingsModel.Description = $"{startUnspecified:MMMM} {year}";
                settingsModel.PeriodMonthDate = startUnspecified;
                settingsModel.PeriodMonthIncludesYear = true;
                settingsModel.AltDescription = $"month {startUnspecified:MMMM} of {year}";
                settingsModel.EndDateTime = settingsModel.StartDateTime.Value.AddMonths(1).AddSeconds(-1);
            }

            settingsModel.PlayDays =
                (int)(settingsModel.EndDateTime.Value - settingsModel.StartDateTime.Value).TotalDays;

            var startDateString = startUnspecified.ToString("yyyy-M-dd");
            var endDateString = settingsModel.EndDateTime.Value.AddHours(-12).ToString("yyyy-M-dd");

            settingsModel.BillboardStartDateTime =
                settingsModel.StartDateTime.Value.AddDays(-settingsModel.PlayDays.Value);
            settingsModel.BillboardEndDateTime =
                settingsModel.EndDateTime.Value.AddDays(-settingsModel.PlayDays.Value);

            settingsModel.UrlParameter = $"from={startDateString}&to={endDateString}";

            settingsModel.TimeFrom = ((DateTimeOffset)settingsModel.StartDateTime).ToUnixTimeSeconds();
            settingsModel.TimeUntil = ((DateTimeOffset)settingsModel.EndDateTime).ToUnixTimeSeconds();

            settingsModel.UseCustomTimePeriod = true;

            if (cachedOnly)
            {
                var twoMonthsAgo = DateTime.UtcNow.AddMonths(-2);
                if (settingsModel.StartDateTime.HasValue && settingsModel.StartDateTime.Value < twoMonthsAgo)
                {
                    settingsModel.TimePeriod = TimePeriod.Monthly;
                    settingsModel.Description = "Monthly";
                    settingsModel.PeriodLabelKey = "shared.period.monthly";
                    settingsModel.PeriodMonthDate = null;
                    settingsModel.PeriodMonthIncludesYear = false;
                    settingsModel.AltDescription = "last month";
                    settingsModel.PlayDays = 30;
                    settingsModel.StartDateTime = DateTime.UtcNow.AddDays(-30);
                    settingsModel.EndDateTime = DateTime.UtcNow;
                    settingsModel.TimeFrom = ((DateTimeOffset)settingsModel.StartDateTime).ToUnixTimeSeconds();
                    settingsModel.UsePlays = false;
                    settingsModel.UseCustomTimePeriod = false;
                }
            }

            return settingsModel;
        }

        var aliases = PeriodAliases.For(language);
        var oneDay = Merge(["1-day", "1day", "1d", "24h", "24-h", "24hr", "24-hr", "24hours"], aliases.OneDay);
        var today = Merge(["today", "day", "daily"], aliases.Today);
        var yesterday = Merge(["yesterday", "yd"], aliases.Yesterday);
        var twoDays = Merge(["2-day", "2day", "2d"], aliases.TwoDays);
        var threeDays = Merge(["3-day", "3day", "3d"], aliases.ThreeDays);
        var fourDays = Merge(["4-day", "4day", "4d"], aliases.FourDays);
        var fiveDays = Merge(["5-day", "5day", "5d"], aliases.FiveDays);
        var sixDays = Merge(["6-day", "6day", "6d"], aliases.SixDays);
        var weekly = Merge(["weekly", "week", "w", "7d"], aliases.Weekly);
        var monthly = Merge(["monthly", "month", "m", "1m", "30d"], aliases.Monthly);
        var quarterly = Merge(["quarterly", "quarter", "q", "3m", "90d"], aliases.Quarterly);
        var halfYearly = Merge(["half-yearly", "halfyearly", "half", "h", "6m", "180d"], aliases.HalfYearly);
        var yearly = Merge(["yearly", "year", "y", "12m", "365d", "1y"], aliases.Yearly);
        var twoYear = Merge(["two-year", "twoyear", "two-yearly", "twoyearly", "2y", "2year", "24m", "730d"], aliases.TwoYear);
        var allTime = Merge(["overall", "alltime", "all-time", "all", "a", "o", "at"], aliases.AllTime);

        if (Contains(options, weekly))
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, weekly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
            settingsModel.TimePeriod = TimePeriod.Weekly;
            settingsModel.Description = "Weekly";
            settingsModel.PeriodLabelKey = "shared.period.weekly";
            settingsModel.AltDescription = "last week";
            settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
            settingsModel.ApiParameter = "7day";
            settingsModel.PlayDays = 7;
        }
        else if (Contains(options, quarterly) && !cachedOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, quarterly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
            settingsModel.TimePeriod = TimePeriod.Quarterly;
            settingsModel.Description = "Quarterly";
            settingsModel.PeriodLabelKey = "shared.period.quarterly";
            settingsModel.AltDescription = "last quarter";
            settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
            settingsModel.ApiParameter = "3month";
            settingsModel.PlayDays = 90;
        }
        else if (Contains(options, halfYearly) && !cachedOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, halfYearly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
            settingsModel.TimePeriod = TimePeriod.Half;
            settingsModel.Description = "Half-yearly";
            settingsModel.PeriodLabelKey = "shared.period.halfYearly";
            settingsModel.AltDescription = "last half year";
            settingsModel.UrlParameter = "date_preset=LAST_180_DAYS";
            settingsModel.ApiParameter = "6month";
            settingsModel.PlayDays = 180;
        }
        else if (Contains(options, monthly))
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, monthly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
            settingsModel.TimePeriod = TimePeriod.Monthly;
            settingsModel.Description = "Monthly";
            settingsModel.PeriodLabelKey = "shared.period.monthly";
            settingsModel.AltDescription = "last month";
            settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
            settingsModel.ApiParameter = "1month";
            settingsModel.PlayDays = 30;
        }
        else if (Contains(options, twoYear) && !cachedOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, twoYear);
            var dateString = localTime.AddDays(-729).ToString("yyyy-M-dd");
            settingsModel.Description = "Two-year";
            settingsModel.PeriodLabelKey = "shared.period.twoYear";
            settingsModel.AltDescription = "last two years";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 730;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-729);
        }
        else if (Contains(options, yearly) && !cachedOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, yearly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
            settingsModel.TimePeriod = TimePeriod.Yearly;
            settingsModel.Description = "Yearly";
            settingsModel.PeriodLabelKey = "shared.period.yearly";
            settingsModel.AltDescription = "last year";
            settingsModel.UrlParameter = "date_preset=LAST_365_DAYS";
            settingsModel.ApiParameter = "12month";
            settingsModel.PlayDays = 365;
        }
        else if (Contains(options, allTime))
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, allTime);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
            settingsModel.TimePeriod = TimePeriod.AllTime;
            settingsModel.Description = "Overall";
            settingsModel.PeriodLabelKey = "shared.period.overall";
            settingsModel.AltDescription = "all-time";
            settingsModel.UrlParameter = "date_preset=ALL";
            settingsModel.ApiParameter = "overall";

            if (registeredLastFm.HasValue)
            {
                settingsModel.PlayDays = (int)(DateTime.UtcNow - registeredLastFm.Value).TotalDays + 1;
                settingsModel.StartDateTime = registeredLastFm.Value.AddDays(-1);
            }
            else
            {
                settingsModel.StartDateTime = new DateTime(2000, 1, 1);
            }
        }
        else if (Contains(options, sixDays) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, sixDays);
            var dateString = localTime.AddDays(-5).ToString("yyyy-M-dd");
            settingsModel.Description = "6-day";
            settingsModel.PeriodLabelKey = "shared.period.sixDay";
            settingsModel.AltDescription = "last 6 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 6;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-5);
        }
        else if (Contains(options, fiveDays) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, fiveDays);
            var dateString = localTime.AddDays(-4).ToString("yyyy-M-dd");
            settingsModel.Description = "5-day";
            settingsModel.PeriodLabelKey = "shared.period.fiveDay";
            settingsModel.AltDescription = "last 5 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 5;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-4);
        }
        else if (Contains(options, fourDays) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, fourDays);
            var dateString = localTime.AddDays(-3).ToString("yyyy-M-dd");
            settingsModel.Description = "4-day";
            settingsModel.PeriodLabelKey = "shared.period.fourDay";
            settingsModel.AltDescription = "last 4 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 4;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-3);
        }
        else if (Contains(options, threeDays) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, threeDays);
            var dateString = localTime.AddDays(-2).ToString("yyyy-M-dd");
            settingsModel.Description = "3-day";
            settingsModel.PeriodLabelKey = "shared.period.threeDay";
            settingsModel.AltDescription = "last 3 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 3;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-2);
        }
        else if (Contains(options, twoDays) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, twoDays);
            var dateString = localTime.AddDays(-1).ToString("yyyy-M-dd");
            settingsModel.Description = "2-day";
            settingsModel.PeriodLabelKey = "shared.period.twoDay";
            settingsModel.AltDescription = "last 2 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 2;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-1);
        }
        else if (Contains(options, yesterday) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, yesterday);
            var dateString = localTime.AddDays(-1).ToString("yyyy-M-dd");
            settingsModel.Description = "yesterday";
            settingsModel.PeriodLabelKey = "shared.period.yesterday";
            settingsModel.AltDescription = "yesterday";
            settingsModel.UrlParameter = $"from={dateString}&to={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 1;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-1);
            settingsModel.EndDateTime = localMidnightInUtc;
        }
        else if (Contains(options, today) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, today);
            var dateString = localTime.ToString("yyyy-M-dd");
            settingsModel.Description = "day";
            settingsModel.PeriodLabelKey = "shared.period.day";
            settingsModel.AltDescription = "day";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 1;
            settingsModel.StartDateTime = localMidnightInUtc;
        }
        else if (Contains(options, oneDay) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, oneDay);
            var dateString = localTime.ToString("yyyy-M-dd");
            settingsModel.Description = "24h";
            settingsModel.PeriodLabelKey = "shared.period.oneDay";
            settingsModel.AltDescription = "24h";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 1;
            settingsModel.StartDateTime = DateTime.UtcNow.AddDays(-1);
        }
        else
        {
            customTimePeriod = false;
        }

        if (customTimePeriod == false)
        {
            settingsModel.DefaultPicked = true;
            if (defaultTimePeriod == TimePeriod.AllTime)
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.TimePeriod = TimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.PeriodLabelKey = "shared.period.overall";
                settingsModel.AltDescription = "all-time";
                settingsModel.UrlParameter = "date_preset=ALL";
                settingsModel.ApiParameter = "overall";

                if (registeredLastFm.HasValue)
                {
                    settingsModel.PlayDays = (int)(DateTime.UtcNow - registeredLastFm.Value).TotalDays + 1;
                    settingsModel.StartDateTime = registeredLastFm.Value.AddDays(-1);
                }
                else
                {
                    settingsModel.StartDateTime = new DateTime(2000, 1, 1);
                }
            }
            else if (defaultTimePeriod == TimePeriod.Yearly)
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.TimePeriod = TimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.PeriodLabelKey = "shared.period.yearly";
                settingsModel.AltDescription = "last year";
                settingsModel.UrlParameter = "date_preset=LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
                settingsModel.PlayDays = 365;
            }
            else if (defaultTimePeriod == TimePeriod.Monthly)
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.TimePeriod = TimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.PeriodLabelKey = "shared.period.monthly";
                settingsModel.AltDescription = "last month";
                settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
                settingsModel.PlayDays = 30;
            }
            else if (defaultTimePeriod == TimePeriod.Quarterly)
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.TimePeriod = TimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.PeriodLabelKey = "shared.period.quarterly";
                settingsModel.AltDescription = "last quarter";
                settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
                settingsModel.PlayDays = 90;
            }
            else
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.TimePeriod = TimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.PeriodLabelKey = "shared.period.weekly";
                settingsModel.AltDescription = "last week";
                settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
                settingsModel.PlayDays = 7;
            }
        }

        if (settingsModel.PlayDays.HasValue)
        {
            var daysToGoBack = settingsModel.PlayDays.Value > 180
                ? 180
                : settingsModel.PlayDays.Value / (settingsModel.PlayDays.Value >= 90 ? 4 : 3);

            settingsModel.BillboardStartDateTime =
                DateTime.UtcNow.AddDays(-(settingsModel.PlayDays.Value + daysToGoBack));
            settingsModel.BillboardEndDateTime =
                DateTime.UtcNow.AddDays(-daysToGoBack);

            settingsModel.PlayDaysWithBillboard = settingsModel.PlayDays.Value + daysToGoBack;
        }

        if (settingsModel.TimePeriod != TimePeriod.AllTime && settingsModel.PlayDays != null && settingsModel.StartDateTime == null)
        {
            var dateAgo = DateTime.UtcNow.AddDays(-settingsModel.PlayDays.Value);
            settingsModel.StartDateTime = dateAgo;
            settingsModel.TimeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
        }
        else if (settingsModel.StartDateTime.HasValue)
        {
            settingsModel.TimeFrom = ((DateTimeOffset)settingsModel.StartDateTime).ToUnixTimeSeconds();
        }

        if (cachedOnly && settingsModel.TimePeriod != TimePeriod.AllTime)
        {
            var twoMonthsAgo = DateTime.UtcNow.AddMonths(-2);
            if (settingsModel.StartDateTime.HasValue && settingsModel.StartDateTime.Value < twoMonthsAgo)
            {
                settingsModel.TimePeriod = TimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.PeriodLabelKey = "shared.period.monthly";
                settingsModel.AltDescription = "last month";
                settingsModel.PlayDays = 30;
                settingsModel.StartDateTime = DateTime.UtcNow.AddDays(-30);
                settingsModel.TimeFrom = ((DateTimeOffset)settingsModel.StartDateTime).ToUnixTimeSeconds();
                settingsModel.UsePlays = false;
                settingsModel.UseCustomTimePeriod = false;
            }
        }

        return settingsModel;
    }

    public static int? GetYear(string extraOptions, bool cleanSetter = true, int minYear = 1900)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return null;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options.Reverse())
        {
            string cleaned;
            if (cleanSetter)
            {
                cleaned = option
                    .Replace("r:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("released:", "", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                cleaned = option;
            }

            if (cleaned.Length == 4 && int.TryParse(cleaned, out var result))
            {
                if (result >= minYear && result <= DateTime.Today.AddDays(1).Year)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static (string monthName, int monthNumber)? GetMonth(string extraOptions, Language language = Language.English)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return null;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options)
        {
            foreach (var month in Months.Where(month => option.ToLower().Equals(month.Key)))
            {
                return (month.Key, month.Value);
            }
        }

        if (language != Language.English)
        {
            var dateTimeFormat = language.GetCultureInfo().DateTimeFormat;
            var excludedMonths = PeriodAliases.For(language).ExcludedMonths;
            foreach (var option in options)
            {
                var lowerOption = option.ToLower();
                if (excludedMonths.Contains(lowerOption))
                {
                    continue;
                }

                for (var monthNumber = 1; monthNumber <= 12; monthNumber++)
                {
                    if (lowerOption.Equals(dateTimeFormat.GetMonthName(monthNumber).ToLower()))
                    {
                        return (lowerOption, monthNumber);
                    }
                }
            }
        }

        return null;
    }

    private static string[] Merge(string[] tokens, string[] aliases)
    {
        return aliases.Length == 0 ? tokens : [.. aliases, .. tokens];
    }

    private static readonly Dictionary<string, int> Months = new()
    {
        { "january", 1 },
        { "jan", 1 },
        { "february", 2 },
        { "feb", 2 },
        { "march", 3 },
        { "mar", 3 },
        { "april", 4 },
        { "apr", 4 },
        { "may", 5 },
        { "june", 6 },
        { "jun", 6 },
        { "july", 7 },
        { "jul", 7 },
        { "august", 8 },
        { "aug", 8 },
        { "september", 9 },
        { "sep", 9 },
        { "october", 10 },
        { "oct", 10 },
        { "november", 11 },
        { "nov", 11 },
        { "december", 12 },
        { "dec", 12 },
    };

    public static bool Contains(string extraOptions, string[] values)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return false;
        }

        var paddedOptions = $" {extraOptions.ToLower()} ";

        foreach (var value in values)
        {
            if (paddedOptions.Contains($" {value.ToLower()} ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string ContainsAndRemove(string extraOptions, string[] values, bool alwaysReturnValue = false)
    {
        extraOptions = extraOptions.ToLower();
        var somethingFound = false;

        foreach (var value in values)
        {
            if (extraOptions.Contains(value.ToLower(), StringComparison.OrdinalIgnoreCase))
            {
                extraOptions = $" {extraOptions} ";
                extraOptions = extraOptions.Replace($" {value.ToLower()} ", " ", StringComparison.OrdinalIgnoreCase);
                extraOptions = extraOptions.Trim();
                somethingFound = true;
            }
        }

        if (somethingFound || alwaysReturnValue)
        {
            return extraOptions.TrimEnd().TrimStart();
        }

        return null;
    }
}
