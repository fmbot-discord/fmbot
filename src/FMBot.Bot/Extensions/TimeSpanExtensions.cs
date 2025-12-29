using System;

namespace FMBot.Bot.Extensions;

public static class TimeSpanExtensions
{
    private static readonly DateTimeOffset DiscordEpoch = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ulong ToSnowflake(this DateTime dateTime)
    {
        return ToSnowflake(new DateTimeOffset(dateTime, TimeSpan.Zero));
    }

    public static ulong ToSnowflake(this DateTimeOffset dateTimeOffset)
    {
        return (ulong)(dateTimeOffset - DiscordEpoch).TotalMilliseconds << 22;
    }

    public static string ToReadableAgeString(this TimeSpan span)
    {
        return string.Format("{0:0}", span.Days / 365.25);
    }

    public static string ToReadableString(this TimeSpan span)
    {
        string formatted = string.Format("{0}{1}{2}{3}",
            span.Duration().Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? string.Empty : "s") : string.Empty,
            span.Duration().Hours > 0 ? string.Format("{0:0} hour{1}, ", span.Hours, span.Hours == 1 ? string.Empty : "s") : string.Empty,
            span.Duration().Minutes > 0 ? string.Format("{0:0} minute{1}, ", span.Minutes, span.Minutes == 1 ? string.Empty : "s") : string.Empty,
            span.Duration().Seconds > 0 ? string.Format("{0:0} second{1}", span.Seconds, span.Seconds == 1 ? string.Empty : "s") : string.Empty);

        if (formatted.EndsWith(", "))
        {
            formatted = formatted.Substring(0, formatted.Length - 2);
        }

        if (string.IsNullOrEmpty(formatted))
        {
            formatted = "0 seconds";
        }

        return formatted;
    }
}
