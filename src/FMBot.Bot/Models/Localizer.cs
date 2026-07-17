using System;
using System.Text;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class Localizer(Language language, NumberFormat numberFormat)
{
    public Language Language { get; } = language;

    public static Localizer ForGuild(ulong? discordGuildId, NumberFormat numberFormat = NumberFormat.NoSeparator, string discordLocale = null)
    {
        return new Localizer(LocalizationService.GetLanguage(discordGuildId, discordLocale), numberFormat);
    }

    public string Translate(string key, params (string Name, string Value)[] args)
    {
        return Interpolate(LocalizationService.GetTranslation(this.Language, key), args);
    }

    public string TranslateCount(string key, long count, params (string Name, string Value)[] args)
    {
        var translation = LocalizationService.GetPluralTranslation(this.Language, key, GetPluralSuffix(this.Language, count));
        return Interpolate(translation.Replace("{{count}}", count.Format(numberFormat)), args);
    }

    public string TimeAgo(DateTime timeAgo)
    {
        var ts = new TimeSpan(Math.Abs(DateTime.UtcNow.Ticks - timeAgo.Ticks));
        var delta = ts.TotalSeconds;

        if (delta < 60)
        {
            return TranslateCount("shared.timeAgo.seconds", ts.Seconds);
        }

        if (delta < 60 * 2)
        {
            return Translate("shared.timeAgo.minute");
        }

        if (delta < 45 * 60)
        {
            return TranslateCount("shared.timeAgo.minutes", ts.Minutes);
        }

        if (delta < 90 * 60)
        {
            return Translate("shared.timeAgo.hour");
        }

        if (delta < 24 * 60 * 60)
        {
            return TranslateCount("shared.timeAgo.hours", ts.Hours);
        }

        if (delta < 48 * 60 * 60)
        {
            return Translate("shared.timeAgo.yesterday");
        }

        if (delta < 30 * 24 * 60 * 60)
        {
            return TranslateCount("shared.timeAgo.days", ts.Days);
        }

        if (delta < 12L * 30 * 24 * 60 * 60)
        {
            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
            return TranslateCount("shared.timeAgo.months", months <= 1 ? 1 : months);
        }

        return Translate("shared.timeAgo.moreThanMonth");
    }

    public string LongListeningTime(TimeSpan timeSpan)
    {
        if (timeSpan.Days >= 1)
        {
            var time = new StringBuilder();
            time.Append(TranslateCount("shared.days", (int)timeSpan.TotalDays));
            if (timeSpan.Hours > 0)
            {
                time.Append($", {TranslateCount("shared.hours", timeSpan.Hours)}");
            }

            return time.ToString();
        }

        if (timeSpan.Hours >= 1)
        {
            var time = new StringBuilder();
            time.Append(TranslateCount("shared.hours", timeSpan.Hours));
            if (timeSpan.Minutes > 0)
            {
                time.Append($", {TranslateCount("shared.minutes", timeSpan.Minutes)}");
            }

            return time.ToString();
        }

        return TranslateCount("shared.minutes", timeSpan.Minutes);
    }

    public string PeriodLabel(TimeSettingsModel timeSettings)
    {
        if (timeSettings.PeriodLabelKey != null)
        {
            return Translate(timeSettings.PeriodLabelKey);
        }

        if (timeSettings.PeriodMonthDate.HasValue)
        {
            var culture = this.Language.GetCultureInfo();
            return timeSettings.PeriodMonthIncludesYear
                ? timeSettings.PeriodMonthDate.Value.ToString(culture.DateTimeFormat.YearMonthPattern, culture)
                : timeSettings.PeriodMonthDate.Value.ToString("MMMM", culture);
        }

        return timeSettings.Description;
    }

    public string AltPeriodLabel(TimeSettingsModel timeSettings)
    {
        if (timeSettings.PeriodLabelKey != null)
        {
            return Translate(timeSettings.PeriodLabelKey.Replace("shared.period.", "shared.periodAlt."));
        }

        return timeSettings.AltDescription;
    }

    public string PeriodLabel(GuildRankingSettings guildListSettings)
    {
        return guildListSettings.TimeSettings != null
            ? PeriodLabel(guildListSettings.TimeSettings)
            : guildListSettings.TimeDescription.ToLower();
    }

    public string MonthName(int month)
    {
        var culture = this.Language.GetCultureInfo();
        return culture.DateTimeFormat.GetMonthName(month);
    }

    public string DayName(DayOfWeek dayOfWeek)
    {
        var culture = this.Language.GetCultureInfo();
        return culture.DateTimeFormat.GetDayName(dayOfWeek);
    }

    public string FormatMonthDay(DateTime date)
    {
        var culture = this.Language.GetCultureInfo();
        return date.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
    }

    public string FormatMonthDayYear(DateTime date)
    {
        var culture = this.Language.GetCultureInfo();
        return date.ToString($"{culture.DateTimeFormat.MonthDayPattern} yyyy", culture);
    }

    public string Ordinal(long amount)
    {
        return Translate(GetOrdinalKey(this.Language, amount), ("count", amount.Format(numberFormat)));
    }

    private static string GetOrdinalKey(Language language, long amount)
    {
        if (language == Language.French)
        {
            return amount == 1 ? "shared.ordinalOne" : "shared.ordinalOther";
        }

        if (language == Language.Swedish)
        {
            var swedishMod10 = amount % 10;
            var swedishMod100 = amount % 100;
            return (swedishMod10 == 1 || swedishMod10 == 2) && swedishMod100 != 11 && swedishMod100 != 12
                ? "shared.ordinalOne"
                : "shared.ordinalOther";
        }

        if (language != Language.English)
        {
            return "shared.ordinalOther";
        }

        var mod10 = amount % 10;
        var mod100 = amount % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "shared.ordinalOne";
        }
        if (mod10 == 2 && mod100 != 12)
        {
            return "shared.ordinalTwo";
        }
        if (mod10 == 3 && mod100 != 13)
        {
            return "shared.ordinalFew";
        }

        return "shared.ordinalOther";
    }

    private static string Interpolate(string translation, (string Name, string Value)[] args)
    {
        if (args == null || args.Length == 0)
        {
            return translation;
        }

        var result = new StringBuilder(translation);
        foreach (var arg in args)
        {
            result.Replace($"{{{{{arg.Name}}}}}", arg.Value ?? string.Empty);
        }

        return result.ToString();
    }

    public static string GetPluralSuffix(Language language, long count)
    {
        var number = Math.Abs(count);
        switch (language)
        {
            case Language.Polish:
            {
                if (number == 1)
                {
                    return "_one";
                }

                var mod10 = number % 10;
                var mod100 = number % 100;
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
                {
                    return "_few";
                }

                return "_many";
            }
            case Language.French:
            case Language.Portuguese:
            {
                if (number == 0 || number == 1)
                {
                    return "_one";
                }
                if (number % 1000000 == 0)
                {
                    return "_many";
                }

                return "_other";
            }
            case Language.Spanish:
            case Language.Italian:
            {
                if (number == 1)
                {
                    return "_one";
                }
                if (number != 0 && number % 1000000 == 0)
                {
                    return "_many";
                }

                return "_other";
            }
            case Language.Hindi:
            {
                return number == 0 || number == 1 ? "_one" : "_other";
            }
            case Language.Indonesian:
            {
                return "_other";
            }
            default:
            {
                return number == 1 ? "_one" : "_other";
            }
        }
    }
}
