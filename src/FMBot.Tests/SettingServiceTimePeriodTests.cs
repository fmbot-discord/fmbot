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
    [TestCase("m", TimePeriod.Monthly, "1month", 30)]
    [TestCase("3m", TimePeriod.Quarterly, "3month", 90)]
    [TestCase("6m", TimePeriod.Half, "6month", 180)]
    [TestCase("365d", TimePeriod.Yearly, "12month", 365)]
    [TestCase("a", TimePeriod.AllTime, "overall", null)]
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
    [TestCase("w radiohead", "radiohead")]
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
    [TestCase(null, 8)]
    [TestCase("radiohead 5", 5)]
    [TestCase("25", 20)]
    [TestCase("0", 8)]
    [TestCase("2023", 8)]
    public void GetAmount_ClampsToMaxAndIgnoresInvalidNumbers(string? options, int expected)
    {
        Assert.That(SettingService.GetAmount(options!, 8, 20), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(null, null)]
    [TestCase("system of a down", "system of a down")]
    [TestCase("panic at the disco", "panic at the disco")]
    public void GetPlaysTimePeriod_NoPeriodOrAllTimeWordInName_KeepsSearchWithoutPeriod(string? options,
        string? expectedSearch)
    {
        var result = SettingService.GetPlaysTimePeriod(options!, null, Language.English);

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchValue, Is.EqualTo(expectedSearch));
            Assert.That(result.SearchValueWithoutPeriod, Is.Null);
            Assert.That(result.TimeSettings, Is.Null);
        });
    }

    [Test]
    [TestCase("a")]
    [TestCase("at")]
    public void GetPlaysTimePeriod_OnlyAllTime_UsesCurrentWithoutPeriod(string options)
    {
        var result = SettingService.GetPlaysTimePeriod(options, null, Language.English);

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchValue, Is.Null);
            Assert.That(result.TimeSettings, Is.Null);
        });
    }

    [Test]
    [TestCase("m", TimePeriod.Monthly)]
    public void GetPlaysTimePeriod_OnlyPeriod_UsesCurrentWithPeriod(string options, TimePeriod expectedPeriod)
    {
        var result = SettingService.GetPlaysTimePeriod(options, null, Language.English);

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchValue, Is.Null);
            Assert.That(result.SearchValueWithoutPeriod, Is.Null);
            Assert.That(result.TimeSettings?.TimePeriod, Is.EqualTo(expectedPeriod));
            Assert.That(result.TimeSettings?.StartDateTime, Is.Not.Null);
        });
    }

    [Test]
    public void GetPlaysTimePeriod_OnlyYear_UsesCurrentWithCalendarYear()
    {
        var result = SettingService.GetPlaysTimePeriod("2024", null, Language.English);

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchValue, Is.Null);
            Assert.That(result.TimeSettings?.StartDateTime, Is.EqualTo(new DateTime(2024, 1, 1)));
            Assert.That(result.TimeSettings?.EndDateTime, Is.EqualTo(new DateTime(2025, 1, 1).AddSeconds(-1)));
        });
    }

    [Test]
    [TestCase("Drake 2025", "drake")]
    [TestCase("Green Day", "green")]
    public void GetPlaysTimePeriod_NameAndPeriod_KeepsBothSearchValues(string options, string expectedWithoutPeriod)
    {
        var result = SettingService.GetPlaysTimePeriod(options, null, Language.English);

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchValue, Is.EqualTo(options));
            Assert.That(result.SearchValueWithoutPeriod, Is.EqualTo(expectedWithoutPeriod));
            Assert.That(result.TimeSettings, Is.Not.Null);
        });
    }

    [Test]
    [TestCase("green day", "green", new[] { "Green Day" }, true)]
    [TestCase("the 1975", "the", new[] { "The 1975" }, true)]
    [TestCase("schoolboy q", "schoolboy", new[] { "ScHoolboy Q" }, true)]
    [TestCase("taylor swift 1989", "taylor swift", new[] { "1989 (Taylor's Version)", "Taylor Swift" }, true)]
    [TestCase("drake 2025", "drake", new[] { "Drake" }, false)]
    [TestCase("utopia july 2025", "utopia", new[] { "UTOPIA", "Travis Scott" }, false)]
    [TestCase("utopia july 2025", "utopia", new[] { "UTOPIA July", "Travis Scott" }, false)]
    public void NameContainsPeriodWords_MatchesWholeWordsInResolvedNames(string searchValue,
        string searchValueWithoutPeriod, string[] names, bool expected)
    {
        Assert.That(SettingService.NameContainsPeriodWords(searchValue, searchValueWithoutPeriod, names),
            Is.EqualTo(expected));
    }

    [Test]
    [TestCase(0L, 3L, 4200L, 2_100_000L, true)]
    [TestCase(5000L, 2_500_000L, 0L, 90_000L, false)]
    [TestCase(0L, 2_500_000L, 0L, 90_000L, false)]
    [TestCase(0L, 12L, 0L, 1_800_000L, true)]
    [TestCase(30L, 40_000L, 2L, 900_000L, false)]
    [TestCase(null, 12L, null, 1_800_000L, true)]
    public void PeriodMatchIsBetter_PrefersUserPlaysThenListeners(long? namePlays, long nameListeners,
        long? periodPlays, long periodListeners, bool expected)
    {
        var nameMatch = new ArtistInfo { UserPlaycount = namePlays, TotalListeners = nameListeners };
        var periodMatch = new ArtistInfo { UserPlaycount = periodPlays, TotalListeners = periodListeners };

        Assert.That(SettingService.PeriodMatchIsBetter(nameMatch, periodMatch), Is.EqualTo(expected));
    }

    [Test]
    public void PeriodMatchIsBetter_NoPeriodMatch_KeepsName()
    {
        var nameMatch = new ArtistInfo { UserPlaycount = 0, TotalListeners = 3 };

        Assert.That(SettingService.PeriodMatchIsBetter(nameMatch, null), Is.False);
    }
}
