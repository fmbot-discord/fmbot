using System.Globalization;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Tests;

public class SettingServiceTimePeriodTests
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(10);
    private CultureInfo _originalCulture = null!;

    [SetUp]
    public void SetUp()
    {
        this._originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = this._originalCulture;
    }

    [Test]
    [TestCase("w", TimePeriod.Weekly, "7day", 7)]
    [TestCase("weekly", TimePeriod.Weekly, "7day", 7)]
    [TestCase("7d", TimePeriod.Weekly, "7day", 7)]
    [TestCase("m", TimePeriod.Monthly, "1month", 30)]
    [TestCase("30d", TimePeriod.Monthly, "1month", 30)]
    [TestCase("q", TimePeriod.Quarterly, "3month", 90)]
    [TestCase("3m", TimePeriod.Quarterly, "3month", 90)]
    [TestCase("h", TimePeriod.Half, "6month", 180)]
    [TestCase("6m", TimePeriod.Half, "6month", 180)]
    [TestCase("y", TimePeriod.Yearly, "12month", 365)]
    [TestCase("1y", TimePeriod.Yearly, "12month", 365)]
    [TestCase("365d", TimePeriod.Yearly, "12month", 365)]
    [TestCase("a", TimePeriod.AllTime, "overall", null)]
    [TestCase("alltime", TimePeriod.AllTime, "overall", null)]
    [TestCase("overall", TimePeriod.AllTime, "overall", null)]
    public void GetTimePeriod_PresetToken_MapsToLastFmPeriod(string option, TimePeriod expectedPeriod,
        string expectedApiParameter, int? expectedPlayDays)
    {
        var result = SettingService.GetTimePeriod(option);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(expectedPeriod));
            Assert.That(result.ApiParameter, Is.EqualTo(expectedApiParameter));
            Assert.That(result.PlayDays, Is.EqualTo(expectedPlayDays));
            Assert.That(result.DefaultPicked, Is.False);
            Assert.That(result.UsePlays, Is.False);
            Assert.That(result.NewSearchValue, Is.Empty);
        });
    }

    [Test]
    [TestCase("1m", TimePeriod.Monthly)]
    [TestCase("12m", TimePeriod.Yearly)]
    [TestCase("24m", null)]
    public void GetTimePeriod_MonthCountTokens_DoNotCollideWithMonthly(string option, TimePeriod? expectedPeriod)
    {
        var result = SettingService.GetTimePeriod(option);

        Assert.Multiple(() =>
        {
            Assert.That(result.DefaultPicked, Is.False);
            if (expectedPeriod.HasValue)
            {
                Assert.That(result.TimePeriod, Is.EqualTo(expectedPeriod));
            }
            else
            {
                Assert.That(result.UseCustomTimePeriod, Is.True);
                Assert.That(result.PlayDays, Is.EqualTo(730));
            }
        });
    }

    [Test]
    public void GetTimePeriod_TokenOnlyMatchesWholeWord_LeavesArtistNameIntact()
    {
        var result = SettingService.GetTimePeriod("the weeknd", TimePeriod.Monthly);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Monthly));
            Assert.That(result.DefaultPicked, Is.True);
            Assert.That(result.NewSearchValue, Is.EqualTo("the weeknd"));
        });
    }

    [Test]
    [TestCase("radiohead w", "radiohead")]
    [TestCase("w radiohead", "radiohead")]
    [TestCase("in rainbows weekly", "in rainbows")]
    [TestCase("the weeknd w", "the weeknd")]
    public void GetTimePeriod_PeriodToken_IsStrippedFromRemainingSearchValue(string option, string expectedSearch)
    {
        var result = SettingService.GetTimePeriod(option);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Weekly));
            Assert.That(result.NewSearchValue, Is.EqualTo(expectedSearch).IgnoreCase);
        });
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("radiohead")]
    public void GetTimePeriod_NoToken_FallsBackToRequestedDefault(string? option)
    {
        var result = SettingService.GetTimePeriod(option!, TimePeriod.Quarterly);

        Assert.Multiple(() =>
        {
            Assert.That(result.DefaultPicked, Is.True);
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Quarterly));
            Assert.That(result.ApiParameter, Is.EqualTo("3month"));
            Assert.That(result.PlayDays, Is.EqualTo(90));
            Assert.That(result.NewSearchValue, Is.EqualTo(option ?? ""));
        });
    }

    [Test]
    public void GetTimePeriod_AllTimeWithRegistrationDate_CoversEntireAccountHistory()
    {
        var registered = DateTime.UtcNow.AddDays(-1000);

        var result = SettingService.GetTimePeriod("alltime", registeredLastFm: registered);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.AllTime));
            Assert.That(result.PlayDays, Is.EqualTo(1001));
            Assert.That(result.StartDateTime, Is.EqualTo(registered.AddDays(-1)));
            Assert.That(result.TimeFrom, Is.EqualTo(((DateTimeOffset)registered.AddDays(-1)).ToUnixTimeSeconds()));
        });
    }

    [Test]
    public void GetTimePeriod_AllTimeWithoutRegistrationDate_StartsAtYear2000()
    {
        var result = SettingService.GetTimePeriod("alltime");

        Assert.Multiple(() =>
        {
            Assert.That(result.StartDateTime, Is.EqualTo(new DateTime(2000, 1, 1)));
            Assert.That(result.PlayDays, Is.Null);
            Assert.That(result.BillboardStartDateTime, Is.Null);
        });
    }

    [Test]
    public void GetTimePeriod_Today_StartsAtUtcMidnightAndUsesPlays()
    {
        var result = SettingService.GetTimePeriod("today");

        Assert.Multiple(() =>
        {
            Assert.That(result.UsePlays, Is.True);
            Assert.That(result.UseCustomTimePeriod, Is.True);
            Assert.That(result.PlayDays, Is.EqualTo(1));
            Assert.That(result.StartDateTime, Is.EqualTo(DateTime.UtcNow.Date));
            Assert.That(result.EndDateTime, Is.EqualTo(DateTime.UtcNow).Within(ClockTolerance));
            Assert.That(result.ApiParameter, Is.Null);
        });
    }

    [Test]
    public void GetTimePeriod_Yesterday_IsBoundedByBothMidnights()
    {
        var result = SettingService.GetTimePeriod("yesterday");

        Assert.Multiple(() =>
        {
            Assert.That(result.StartDateTime, Is.EqualTo(DateTime.UtcNow.Date.AddDays(-1)));
            Assert.That(result.EndDateTime, Is.EqualTo(DateTime.UtcNow.Date));
            Assert.That(result.PlayDays, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetTimePeriod_TodayInUserTimeZone_StartsAtLocalMidnightConvertedToUtc()
    {
        const string timeZone = "America/New_York";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var expectedStart = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, tz);

        var result = SettingService.GetTimePeriod("today", timeZone: timeZone);

        Assert.Multiple(() =>
        {
            Assert.That(result.StartDateTime, Is.EqualTo(expectedStart));
            Assert.That(result.UrlParameter, Is.EqualTo($"from={localNow:yyyy-M-dd}"));
        });
    }

    [Test]
    public void GetTimePeriod_UnknownTimeZone_FallsBackToUtc()
    {
        var result = SettingService.GetTimePeriod("today", timeZone: "Not/AZone");

        Assert.That(result.StartDateTime, Is.EqualTo(DateTime.UtcNow.Date));
    }

    [Test]
    public void GetTimePeriod_DailyPeriodsDisabled_IgnoresDailyTokens()
    {
        var result = SettingService.GetTimePeriod("today", dailyTimePeriods: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.DefaultPicked, Is.True);
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Weekly));
            Assert.That(result.UsePlays, Is.False);
            Assert.That(result.NewSearchValue, Is.EqualTo("today"));
        });
    }

    [Test]
    public void GetTimePeriod_Year_CoversCalendarYearInclusive()
    {
        var result = SettingService.GetTimePeriod("2023");

        Assert.Multiple(() =>
        {
            Assert.That(result.UseCustomTimePeriod, Is.True);
            Assert.That(result.StartDateTime, Is.EqualTo(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.EndDateTime, Is.EqualTo(new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
            Assert.That(result.TimeFrom, Is.EqualTo(1672531200));
            Assert.That(result.TimeUntil, Is.EqualTo(1704067199));
            Assert.That(result.UrlParameter, Is.EqualTo("from=2023-1-01&to=2023-12-31"));
            Assert.That(result.Description, Is.EqualTo("2023"));
            Assert.That(result.BillboardTimeDescription, Is.EqualTo("2022"));
            Assert.That(result.NewSearchValue, Is.Empty);
        });
    }

    [Test]
    public void GetTimePeriod_MonthWithYear_CoversThatMonthOnly()
    {
        var result = SettingService.GetTimePeriod("radiohead march 2024");

        Assert.Multiple(() =>
        {
            Assert.That(result.StartDateTime, Is.EqualTo(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.EndDateTime, Is.EqualTo(new DateTime(2024, 3, 31, 23, 59, 59, DateTimeKind.Utc)));
            Assert.That(result.Description, Is.EqualTo("March 2024"));
            Assert.That(result.PeriodMonthDate, Is.EqualTo(new DateTime(2024, 3, 1)));
            Assert.That(result.PeriodMonthIncludesYear, Is.True);
            Assert.That(result.NewSearchValue, Is.EqualTo("radiohead").IgnoreCase);
        });
    }

    [Test]
    public void GetTimePeriod_MonthWithoutYear_UsesMostRecentOccurrenceOfThatMonth()
    {
        var now = DateTime.UtcNow;
        var expectedDecemberYear = now.Month == 12 ? now.Year : now.Year - 1;

        var january = SettingService.GetTimePeriod("january");
        var december = SettingService.GetTimePeriod("december");

        Assert.Multiple(() =>
        {
            Assert.That(january.StartDateTime, Is.EqualTo(new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(january.PeriodMonthIncludesYear, Is.False);
            Assert.That(december.StartDateTime,
                Is.EqualTo(new DateTime(expectedDecemberYear, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(december.EndDateTime,
                Is.EqualTo(new DateTime(expectedDecemberYear, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void GetTimePeriod_MonthInUserTimeZone_StartsAtLocalMidnight()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        var result = SettingService.GetTimePeriod("march 2024", timeZone: "Asia/Tokyo");

        Assert.That(result.StartDateTime,
            Is.EqualTo(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 3, 1), tz)));
    }

    [Test]
    public void GetTimePeriod_YearWhenCachedOnly_DowngradesToMonthly()
    {
        var result = SettingService.GetTimePeriod("2023", cachedOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Monthly));
            Assert.That(result.UseCustomTimePeriod, Is.False);
            Assert.That(result.PlayDays, Is.EqualTo(30));
            Assert.That(result.PeriodMonthDate, Is.Null);
            Assert.That(result.StartDateTime, Is.EqualTo(DateTime.UtcNow.AddDays(-30)).Within(ClockTolerance));
        });
    }

    [Test]
    [TestCase("y")]
    [TestCase("q")]
    [TestCase("h")]
    [TestCase("2y")]
    public void GetTimePeriod_LongPresetWhenCachedOnly_IsNotHonoured(string option)
    {
        var result = SettingService.GetTimePeriod(option, cachedOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.DefaultPicked, Is.True);
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Weekly));
            Assert.That(result.NewSearchValue, Is.EqualTo(option));
        });
    }

    [Test]
    [TestCase("w", 7, 2)]
    [TestCase("m", 30, 10)]
    [TestCase("q", 90, 22)]
    [TestCase("h", 180, 45)]
    [TestCase("y", 365, 180)]
    public void GetTimePeriod_BillboardWindow_IsShiftedBackProportionally(string option, int playDays, int shift)
    {
        var result = SettingService.GetTimePeriod(option);

        Assert.Multiple(() =>
        {
            Assert.That(result.PlayDaysWithBillboard, Is.EqualTo(playDays + shift));
            Assert.That(result.BillboardEndDateTime, Is.EqualTo(DateTime.UtcNow.AddDays(-shift)).Within(ClockTolerance));
            Assert.That(result.BillboardStartDateTime,
                Is.EqualTo(DateTime.UtcNow.AddDays(-(playDays + shift))).Within(ClockTolerance));
        });
    }

    [Test]
    public void GetTimePeriod_LocalizedToken_ResolvesWhenLanguageIsSet()
    {
        var result = SettingService.GetTimePeriod("wöchentlich", language: Language.German);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimePeriod, Is.EqualTo(TimePeriod.Weekly));
            Assert.That(result.NewSearchValue, Is.Empty);
        });
    }

    [Test]
    [TestCase("the weeknd", new[] { "w" }, false)]
    [TestCase("w", new[] { "w" }, true)]
    [TestCase("radiohead W", new[] { "w" }, true)]
    [TestCase("  w  ", new[] { "w" }, true)]
    [TestCase("", new[] { "w" }, false)]
    [TestCase(null, new[] { "w" }, false)]
    public void Contains_MatchesWholeWordsCaseInsensitively(string? options, string[] values, bool expected)
    {
        Assert.That(SettingService.Contains(options!, values), Is.EqualTo(expected));
    }

    [Test]
    public void ContainsAndRemove_RemovesOnlyTheMatchedWord()
    {
        var result = SettingService.ContainsAndRemove("Radiohead w", ["w"]);

        Assert.That(result, Is.EqualTo("radiohead").IgnoreCase);
    }

    [Test]
    public void ContainsAndRemove_NothingMatched_ReturnsNullUnlessForced()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingService.ContainsAndRemove("radiohead", ["x"]), Is.Null);
            Assert.That(SettingService.ContainsAndRemove("radiohead", ["x"], alwaysReturnValue: true),
                Is.EqualTo("radiohead"));
        });
    }

    [Test]
    [TestCase(null, 8)]
    [TestCase("", 8)]
    [TestCase("10", 10)]
    [TestCase("radiohead 5", 5)]
    [TestCase("25", 20)]
    [TestCase("0", 8)]
    [TestCase("-3", 8)]
    [TestCase("101", 8)]
    [TestCase("2023", 8)]
    public void GetAmount_ClampsToMaxAndIgnoresInvalidNumbers(string? options, int expected)
    {
        Assert.That(SettingService.GetAmount(options!, 8, 20), Is.EqualTo(expected));
    }
}
