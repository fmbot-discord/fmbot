using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Google.Protobuf.WellKnownTypes;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services;

public class SettingService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public SettingService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public static TimeSettingsModel GetTimePeriod(string options,
        TimePeriod defaultTimePeriod = TimePeriod.Weekly,
        DateTime? registeredLastFm = null,
        bool cachedOrAllTimeOnly = false,
        bool dailyTimePeriods = true,
        string timeZone = null)
    {
        var settingsModel = new TimeSettingsModel();
        bool? customTimePeriod = null;

        options ??= "";
        settingsModel.NewSearchValue = options;
        settingsModel.UsePlays = false;
        settingsModel.UseCustomTimePeriod = false;
        settingsModel.EndDateTime = DateTime.UtcNow;
        settingsModel.DefaultPicked = false;

        var timeZoneInfo = timeZone != null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), timeZoneInfo);
        var localMidnightInUtc = TimeZoneInfo.ConvertTimeToUtc(localTime.Date, timeZoneInfo);

        var year = GetYear(options, false);
        var month = GetMonth(options);

        if ((year != null || month != null) && !cachedOrAllTimeOnly)
        {
            var startUnspecified = new DateTime(
                year.GetValueOrDefault(DateTime.UtcNow.Year),
                month.GetValueOrDefault(1),
                1);

            settingsModel.StartDateTime = TimeZoneInfo.ConvertTimeToUtc(startUnspecified.Date, timeZoneInfo);

            if (month.HasValue && month.Value > localTime.Month && !year.HasValue)
            {
                settingsModel.StartDateTime = settingsModel.StartDateTime.Value.AddYears(-1);
                year = settingsModel.StartDateTime.Value.Year;
            }

            if (year.HasValue && !month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { year.Value.ToString() });
                settingsModel.Description = $"{year}";
                settingsModel.AltDescription = $"year {year}";
                settingsModel.BillboardTimeDescription = $"{year - 1}";
                settingsModel.EndDateTime = settingsModel.StartDateTime.Value.AddYears(1).AddSeconds(-1);
            }
            if (!year.HasValue && month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { month.Value.ToString(), DateTimeFormatInfo.CurrentInfo.GetMonthName(month.Value) });
                settingsModel.Description = startUnspecified.ToString("MMMM");
                settingsModel.AltDescription = $"month {startUnspecified.ToString("MMMM")}";
                settingsModel.EndDateTime = settingsModel.StartDateTime.Value.AddMonths(1).AddSeconds(-1);
                settingsModel.BillboardTimeDescription = $"{startUnspecified.AddMonths(-1):MMMM}";
            }
            if (year.HasValue && month.HasValue)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { year.Value.ToString(), month.Value.ToString(), DateTimeFormatInfo.CurrentInfo.GetMonthName(month.Value) });

                settingsModel.Description = $"{startUnspecified:MMMM} {year}";
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

            return settingsModel;
        }

        var oneDay = new[] { "1-day", "1day", "1d", "24h", "24-h", "24hr", "24-hr", "24hours" };
        var today = new[] { "today", "day", "daily" };
        var yesterday = new[] { "yesterday", "yd" };
        var twoDays = new[] { "2-day", "2day", "2d" };
        var threeDays = new[] { "3-day", "3day", "3d" };
        var fourDays = new[] { "4-day", "4day", "4d" };
        var fiveDays = new[] { "5-day", "5day", "5d" };
        var sixDays = new[] { "6-day", "6day", "6d" };
        var weekly = new[] { "weekly", "week", "w", "7d" };
        var monthly = new[] { "monthly", "month", "m", "1m", "30d" };
        var quarterly = new[] { "quarterly", "quarter", "q", "3m", "90d" };
        var halfYearly = new[] { "half-yearly", "halfyearly", "half", "h", "6m", "180d" };
        var yearly = new[] { "yearly", "year", "y", "12m", "365d", "1y" };
        var allTime = new[] { "overall", "alltime", "all-time", "all", "a", "o", "at" };

        if (Contains(options, weekly))
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, weekly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
            settingsModel.TimePeriod = TimePeriod.Weekly;
            settingsModel.Description = "Weekly";
            settingsModel.AltDescription = "last week";
            settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
            settingsModel.ApiParameter = "7day";
            settingsModel.PlayDays = 7;
        }
        else if (Contains(options, monthly))
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, monthly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
            settingsModel.TimePeriod = TimePeriod.Monthly;
            settingsModel.Description = "Monthly";
            settingsModel.AltDescription = "last month";
            settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
            settingsModel.ApiParameter = "1month";
            settingsModel.PlayDays = 30;
        }
        else if (Contains(options, quarterly) && !cachedOrAllTimeOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, quarterly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
            settingsModel.TimePeriod = TimePeriod.Quarterly;
            settingsModel.Description = "Quarterly";
            settingsModel.AltDescription = "last quarter";
            settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
            settingsModel.ApiParameter = "3month";
            settingsModel.PlayDays = 90;
        }
        else if (Contains(options, halfYearly) && !cachedOrAllTimeOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, halfYearly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
            settingsModel.TimePeriod = TimePeriod.Half;
            settingsModel.Description = "Half-yearly";
            settingsModel.AltDescription = "last half year";
            settingsModel.UrlParameter = "date_preset=LAST_180_DAYS";
            settingsModel.ApiParameter = "6month";
            settingsModel.PlayDays = 180;
        }
        else if (Contains(options, yearly) && !cachedOrAllTimeOnly)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, yearly);
            settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
            settingsModel.TimePeriod = TimePeriod.Yearly;
            settingsModel.Description = "Yearly";
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
            settingsModel.AltDescription = "last 2 days";
            settingsModel.UrlParameter = $"from={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 2;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-1);
        }
        else if (Contains(options, yesterday) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, oneDay);
            var dateString = localTime.AddDays(-1).ToString("yyyy-M-dd");
            settingsModel.Description = "yesterday";
            settingsModel.AltDescription = "yesterday";
            settingsModel.UrlParameter = $"from={dateString}&to={dateString}";
            settingsModel.UsePlays = true;
            settingsModel.UseCustomTimePeriod = true;
            settingsModel.PlayDays = 2;
            settingsModel.StartDateTime = localMidnightInUtc.AddDays(-1);
            settingsModel.EndDateTime = localMidnightInUtc;
        }
        else if (Contains(options, today) && dailyTimePeriods)
        {
            settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, today);
            var dateString = localTime.ToString("yyyy-M-dd");
            settingsModel.Description = "day";
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
                settingsModel.AltDescription = "last week";
                settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
                settingsModel.PlayDays = 7;
            }
        }

        if (settingsModel.PlayDays.HasValue)
        {
            var daysToGoBack = settingsModel.PlayDays.Value > 180 ? 180 : settingsModel.PlayDays.Value / (settingsModel.PlayDays.Value >= 90 ? 4 : 3);

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

        return settingsModel;
    }

    public static DiscogsCollectionSettings SetDiscogsCollectionSettings(string extraOptions = null)
    {
        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = new List<DiscogsFormat>(),
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return collectionSettings;
        }

        var searchTerms = collectionSettings.NewSearchValue.Split(' ');

        foreach (var word in searchTerms)
        {
            var discogsFormat = DiscogsCollectionSettings.ToDiscogsFormat(word);
            if (discogsFormat.value != null)
            {
                collectionSettings.NewSearchValue = ContainsAndRemove(collectionSettings.NewSearchValue, new[] { word });
                collectionSettings.Formats.Add(discogsFormat.format);
            }
        }

        var miscFormats = new[] { "misc", "miscellaneous" };
        if (Contains(extraOptions, miscFormats))
        {
            collectionSettings.NewSearchValue = ContainsAndRemove(collectionSettings.NewSearchValue, miscFormats);
            collectionSettings.Formats.Add(DiscogsFormat.Miscellaneous);
        }

        collectionSettings.Formats = collectionSettings.Formats.Distinct().ToList();

        return collectionSettings;
    }

    public static TopListSettings SetTopListSettings(string extraOptions = null)
    {
        var topListSettings = new TopListSettings
        {
            Billboard = false,
            EmbedSize = EmbedSize.Default,
            Discogs = false,
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return topListSettings;
        }

        var billboard = new[] { "bb", "billboard", "compare" };
        if (Contains(extraOptions, billboard))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, billboard);
            topListSettings.Billboard = true;
        }

        var extraLarge = new[] { "xl", "xxl", "extralarge" };
        var extraSmall = new[] { "xs", "xxs", "extrasmall" };
        if (Contains(extraOptions, extraLarge))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, extraLarge);
            topListSettings.EmbedSize = EmbedSize.Large;
        }
        else if (Contains(extraOptions, extraSmall))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, extraSmall);
            topListSettings.EmbedSize = EmbedSize.Small;
        }

        var discogs = new[] { "dc", "discogs" };
        if (Contains(extraOptions, discogs))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, discogs);
            topListSettings.Discogs = true;
        }
        var timeListened = new[] { "tl", "timelistened" };
        if (Contains(extraOptions, timeListened))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, timeListened);
            topListSettings.Type = TopListType.TimeListened;
        }

        foreach (var option in extraOptions.Split(" "))
        {
            if (option.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                option.StartsWith("released:", StringComparison.OrdinalIgnoreCase))
            {
                var yearString = option
                    .Replace("r:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("released:", "", StringComparison.OrdinalIgnoreCase);

                if (int.TryParse(yearString, out var year) && year <= DateTime.UtcNow.Year && year >= 1900)
                {
                    topListSettings.ReleaseYearFilter = year;
                    topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, [$"r:{year}", $"released:{year}", option]);
                }
            }
            if (option.StartsWith("d:", StringComparison.OrdinalIgnoreCase) ||
                option.StartsWith("decade:", StringComparison.OrdinalIgnoreCase))
            {
                var yearString = option
                    .Replace("d:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("decade:", "", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('s')
                    .TrimEnd('S');

                if (int.TryParse(yearString, out var year))
                {
                    if (year < 100)
                    {
                        year += year < 30 ? 2000 : 1900;
                    }

                    year = (year / 10) * 10;

                    if (year <= DateTime.UtcNow.Year && year >= 1900)
                    {
                        topListSettings.ReleaseDecadeFilter = year;
                        topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue,
                            [$"d:{year}", $"decade:{year}", $"d:{year}s", $"decade:{year}s", option]);
                    }
                }
            }
        }

        return topListSettings;
    }

    public static (ResponseMode mode, string newSearchValue) SetMode(string extraOptions, ResponseMode? userMode)
    {
        var newSearchValue = extraOptions;

        var image = new[] { "img", "image" };
        if (Contains(extraOptions, image))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, image);
            userMode = ResponseMode.Image;
        }
        var embed = new[] { "embed", "text", "txt" };
        if (Contains(extraOptions, embed))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embed);
            userMode = ResponseMode.Embed;
        }

        userMode ??= ResponseMode.Embed;

        return (userMode.Value, newSearchValue);
    }

    public static WhoKnowsSettings SetWhoKnowsSettings(WhoKnowsSettings currentWhoKnowsSettings, string extraOptions, UserType userType = UserType.User)
    {
        var whoKnowsSettings = currentWhoKnowsSettings;

        if (extraOptions == null)
        {
            return whoKnowsSettings;
        }

        var mode = SetMode(extraOptions, currentWhoKnowsSettings.ResponseMode);

        whoKnowsSettings.ResponseMode = mode.mode;
        whoKnowsSettings.NewSearchValue = mode.newSearchValue;

        var hidePrivateUsers = new[] { "hp", "hideprivate", "hideprivateusers" };
        if (Contains(extraOptions, hidePrivateUsers))
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, hidePrivateUsers);
            whoKnowsSettings.HidePrivateUsers = true;
        }

        var adminView = new[] { "av", "adminview" };
        if (Contains(extraOptions, adminView) && userType is UserType.Admin or UserType.Owner)
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, adminView);
            whoKnowsSettings.AdminView = true;
        }

        var roleFilter = new[] { "rf", "rolefilter", "rolepicker", "roleselector" };
        if (Contains(extraOptions, roleFilter))
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, roleFilter);
            whoKnowsSettings.DisplayRoleFilter = true;
        }

        var qualityFilter = new[] { "nf", "nofilter" };
        if (Contains(extraOptions, qualityFilter))
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, qualityFilter);
            whoKnowsSettings.QualityFilterDisabled = true;
        }

        var (enabled, newSearchValue) = RedirectsEnabled(whoKnowsSettings.NewSearchValue);
        whoKnowsSettings.RedirectsEnabled = enabled;
        whoKnowsSettings.NewSearchValue = newSearchValue;

        return whoKnowsSettings;
    }

    public static (bool Enabled, string NewSearchValue) OrderByPlaycount(string extraOptions)
    {
        var noRedirect = new[] { "plays", "playcount", "orderbyplays", "orderbyplaycount" };
        if (Contains(extraOptions, noRedirect))
        {
            return (true, ContainsAndRemove(extraOptions, noRedirect));
        }

        return (false, extraOptions);
    }

    public static (bool Enabled, string NewSearchValue) RedirectsEnabled(string extraOptions)
    {
        var noRedirect = new[] { "nr", "noredirect" };
        if (Contains(extraOptions, noRedirect))
        {
            return (false, ContainsAndRemove(extraOptions, noRedirect));
        }

        return (true, extraOptions);
    }

    public static (bool User, string NewSearchValue) IsUserView(string extraOptions)
    {
        var guild = new[] { "server", "guild" };
        if (Contains(extraOptions, guild))
        {
            return (false, ContainsAndRemove(extraOptions, guild));
        }

        return (true, extraOptions);
    }

    public static (UpdateType updateType, bool optionPicked, string description) GetUpdateType(string extraOptions)
    {
        var updateType = new UpdateType();
        var optionPicked = false;
        var description = new StringBuilder();

        var full = new[] { "full", "force", "f" };
        if (Contains(extraOptions, full))
        {
            updateType |= UpdateType.Full;
            optionPicked = true;
            description.AppendLine("- Everything (full update)");
        }
        else
        {
            var allPlays = new[] { "plays", "allplays" };
            if (Contains(extraOptions, allPlays))
            {
                updateType |= UpdateType.AllPlays;
                optionPicked = true;
                description.AppendLine("- All scrobbles");
            }

            var artists = new[] { "artists", "artist", "a" };
            if (Contains(extraOptions, artists))
            {
                updateType |= UpdateType.Artists;
                optionPicked = true;
                description.AppendLine("- Your top artists");
            }

            var albums = new[] { "albums", "album", "ab" };
            if (Contains(extraOptions, albums))
            {
                updateType |= UpdateType.Albums;
                optionPicked = true;
                description.AppendLine("- Your top albums");
            }

            var tracks = new[] { "tracks", "track", "tr" };
            if (Contains(extraOptions, tracks))
            {
                updateType |= UpdateType.Tracks;
                optionPicked = true;
                description.AppendLine("- Your top tracks");
            }
        }

        // var discogs = new[] { "discogs", "discog", "vinyl", "collection" };
        // if (Contains(extraOptions, discogs))
        // {
        //     updateType |= UpdateType.Discogs;
        //     optionPicked = true;
        //     description.AppendLine("- Your Discogs collection");
        // }

        return (updateType, optionPicked, description.ToString());
    }

    public async Task<UserSettingsModel> GetUser(
        string extraOptions,
        User user,
        ICommandContext context,
        bool firstOptionIsLfmUsername = false)
    {
        return await GetUser(extraOptions, user, context.Guild, context.User, firstOptionIsLfmUsername);
    }

    public async Task<UserSettingsModel> GetUser(
        string extraOptions,
        User user,
        IGuild discordGuild,
        IUser discordUser,
        bool firstOptionIsLfmUsername = false,
        bool allowNonFmbot = false)
    {
        string discordUserName;
        if (discordGuild != null)
        {
            var discordGuildUser = await discordGuild.GetUserAsync(user.DiscordUserId, CacheMode.CacheOnly);
            discordUserName = discordGuildUser?.DisplayName ?? discordUser.GlobalName ?? discordUser.Username;
        }
        else
        {
            discordUserName = discordUser.GlobalName ?? discordUser.Username;
        }

        var settingsModel = new UserSettingsModel
        {
            DifferentUser = false,
            TimeZone = user.TimeZone,
            UserNameLastFm = user.UserNameLastFM,
            SessionKeyLastFm = user.SessionKeyLastFm,
            DiscordUserId = discordUser.Id,
            DisplayName = discordUserName,
            UserId = user.UserId,
            UserType = user.UserType,
            RegisteredLastFm = user.RegisteredLastFm,
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return settingsModel;
        }

        var options = extraOptions.Split(' ');

        if (firstOptionIsLfmUsername && !string.IsNullOrWhiteSpace(options.First()))
        {
            var otherUser = await GetDifferentUser(options.First());

            if (otherUser != null)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { options.First() }, true);

                settingsModel.DisplayName = otherUser.UserNameLastFM;
                settingsModel.TimeZone = otherUser.TimeZone ?? user.TimeZone;
                settingsModel.DifferentUser = true;
                settingsModel.DiscordUserId = otherUser.DiscordUserId;
                settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                settingsModel.SessionKeyLastFm = otherUser.SessionKeyLastFm;
                settingsModel.UserType = otherUser.UserType;
                settingsModel.UserId = otherUser.UserId;
                settingsModel.RegisteredLastFm = otherUser.RegisteredLastFm;

                return settingsModel;
            }

            if (allowNonFmbot)
            {
                if (options.First().Length is >= 3 and <= 15)
                {
                    var lfmUserName = options.First().ToLower();

                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, [lfmUserName], true);
                    settingsModel.UserNameLastFm = lfmUserName;
                    settingsModel.DisplayName = lfmUserName;
                    settingsModel.SessionKeyLastFm = null;
                    settingsModel.RegisteredLastFm = null;
                    settingsModel.UserId = 0;
                    settingsModel.DifferentUser = true;

                    return settingsModel;
                }
            }
        }

        foreach (var option in options)
        {
            var otherUser = await DiscordIdToUser(option);

            if (otherUser != null)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { "<@&", "<", "@", "!", ">", "<@&",
                    otherUser.DiscordUserId.ToString(), $"<@!{otherUser.DiscordUserId}>", $"<@{otherUser.DiscordUserId}>", $"<@&{otherUser.DiscordUserId}>",
                    otherUser.UserNameLastFM.ToLower() }, true);

                if (discordGuild != null)
                {
                    var discordGuildUser = await discordGuild.GetUserAsync(otherUser.DiscordUserId, CacheMode.CacheOnly);
                    settingsModel.DisplayName = discordGuildUser != null ? discordGuildUser.DisplayName : otherUser.UserNameLastFM;
                }
                else
                {
                    settingsModel.DisplayName = otherUser.UserNameLastFM;
                }

                settingsModel.DifferentUser = true;
                settingsModel.DiscordUserId = otherUser.DiscordUserId;
                settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                settingsModel.TimeZone = otherUser.TimeZone ?? user.TimeZone;
                settingsModel.UserType = otherUser.UserType;
                settingsModel.UserId = otherUser.UserId;
                settingsModel.RegisteredLastFm = otherUser.RegisteredLastFm;
            }

            if (option.StartsWith("lfm:") && option.Length > 4)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { "lfm:" }, true);

                var lfmUserName = option.ToLower().Replace("lfm:", "");

                var foundLfmUser = await GetDifferentUser(lfmUserName);

                if (foundLfmUser != null)
                {
                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { lfmUserName, $"lfm:{lfmUserName}" }, true);

                    settingsModel.DisplayName = foundLfmUser.UserNameLastFM;
                    settingsModel.TimeZone = foundLfmUser.TimeZone ?? user.TimeZone;
                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = foundLfmUser.DiscordUserId;
                    settingsModel.UserNameLastFm = foundLfmUser.UserNameLastFM;
                    settingsModel.SessionKeyLastFm = foundLfmUser.SessionKeyLastFm;
                    settingsModel.UserType = foundLfmUser.UserType;
                    settingsModel.UserId = foundLfmUser.UserId;
                    settingsModel.RegisteredLastFm = foundLfmUser.RegisteredLastFm;

                    return settingsModel;
                }

                if (allowNonFmbot)
                {
                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { lfmUserName }, true);
                    settingsModel.UserNameLastFm = lfmUserName;
                    settingsModel.DisplayName = lfmUserName;
                    settingsModel.SessionKeyLastFm = null;
                    settingsModel.RegisteredLastFm = null;
                    settingsModel.UserId = 0;
                    settingsModel.DifferentUser = true;

                    return settingsModel;
                }
            }
        }

        return settingsModel;
    }

    public async Task<UserSettingsModel> GetOriginalContextUser(
        ulong discordUserId, ulong requesterUserId, IGuild discordGuild, IUser contextDiscordUser)
    {
        IGuildUser guildUser = null;
        if (discordGuild != null)
        {
            guildUser = await discordGuild.GetUserAsync(discordUserId, CacheMode.CacheOnly);
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var targetUser = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        var differentUser = discordUserId != requesterUserId;

        return new UserSettingsModel
        {
            DiscordUserId = targetUser.DiscordUserId,
            DifferentUser = differentUser,
            TimeZone = targetUser.TimeZone,
            UserId = targetUser.UserId,
            DisplayName = guildUser?.DisplayName ??
                          (differentUser ? targetUser.UserNameLastFM : contextDiscordUser.GlobalName ?? contextDiscordUser.Username),
            RegisteredLastFm = targetUser.RegisteredLastFm,
            SessionKeyLastFm = targetUser.SessionKeyLastFm,
            UserNameLastFm = targetUser.UserNameLastFM,
            UserType = targetUser.UserType
        };
    }

    public async Task<User> GetDifferentUser(string searchValue)
    {
        var otherUser = await DiscordIdToUser(searchValue);

        if (otherUser == null)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            searchValue = searchValue.ToLower().Replace("lfm:", "");
            return await db.Users
                .AsQueryable()
                .OrderByDescending(o => o.LastUsed != null)
                .ThenByDescending(o => o.LastUsed)
                .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == searchValue);
        }

        return otherUser;
    }

    private async Task<User> DiscordIdToUser(string value)
    {
        if (!value.Contains("<@") && value.Length is < 17 or > 19)
        {
            return null;
        }

        var id = value.Trim('@', '!', '<', '>', '&');

        if (!ulong.TryParse(id, out var discordUserId))
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public static int GetAmount(
        string extraOptions,
        int amount = 8,
        int maxAmount = 20)
    {
        if (extraOptions == null)
        {
            return amount;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options)
        {
            if (int.TryParse(option, out var result))
            {
                if (result > 0 && result <= 100)
                {
                    if (result > maxAmount)
                    {
                        return maxAmount;
                    }

                    return result;
                }
            }
        }

        return amount;
    }

    public static int? GetYear(string extraOptions, bool cleanSetter = true)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return null;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options)
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
                if (result >= 1900 && result <= DateTime.Today.AddDays(1).Year)
                {
                    return result;
                }
            }
        }

        return null;
    }

    public static int? GetMonth(string extraOptions)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return null;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options)
        {
            foreach (var month in Months.Where(month => option.ToLower().StartsWith(month.Key)))
            {
                return month.Value;
            }
        }

        return null;
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

    public static long GetGoalAmount(
        string extraOptions,
        long currentPlaycount)
    {
        var goalAmount = 100;
        var ownGoalSet = false;

        if (extraOptions != null)
        {
            var options = extraOptions
                .Replace("(", "")
                .Replace(")", "")
                .Replace("*", "")
                .Replace("`", "")
                .Split(' ');

            foreach (var option in options)
            {
                if (option.ToLower().EndsWith("k"))
                {
                    if (int.TryParse(option.ToLower().Replace("k", ""), out var kResult))
                    {
                        kResult *= 1000;
                        if (kResult > currentPlaycount)
                        {
                            goalAmount = kResult;
                            ownGoalSet = true;
                            break;
                        }
                    }
                }
                else if (int.TryParse(option, out var result) && result > currentPlaycount)
                {
                    goalAmount = result;
                    ownGoalSet = true;
                }
            }
        }


        if (!ownGoalSet)
        {
            foreach (var breakPoint in Constants.PlayCountBreakPoints)
            {
                if (currentPlaycount < breakPoint)
                {
                    goalAmount = breakPoint;
                    break;
                }
            }
        }

        if (goalAmount > 10000000)
        {
            goalAmount = 10000000;
        }

        return goalAmount;
    }

    public static (int amount, bool isRandom) GetMilestoneAmount(
        string extraOptions,
        long currentPlaycount)
    {
        var goalAmount = 100;
        var ownGoalSet = false;
        var isRandom = false;

        if (extraOptions != null)
        {
            var options = extraOptions
                .Replace("(", "")
                .Replace(")", "")
                .Replace("*", "")
                .Replace("`", "")
                .Split(' ');

            foreach (var option in options)
            {
                if (option.ToLower().EndsWith("k"))
                {
                    if (int.TryParse(option.ToLower().Replace("k", ""), out var kResult))
                    {
                        kResult *= 1000;
                        if (kResult < currentPlaycount)
                        {
                            goalAmount = kResult;
                            ownGoalSet = true;
                            break;
                        }
                    }
                }
                else if (int.TryParse(option, out var result) && result < currentPlaycount)
                {
                    goalAmount = result;
                    ownGoalSet = true;
                    break;
                }

                if (option.ToLower().Contains("random") || option.ToLower().Contains("rnd"))
                {
                    goalAmount = RandomNumberGenerator.GetInt32(1, (int)currentPlaycount);
                    ownGoalSet = true;
                    isRandom = true;
                    break;
                }
            }
        }

        if (!ownGoalSet)
        {
            foreach (var breakPoint in Constants.PlayCountBreakPoints.OrderByDescending(o => o))
            {
                if (currentPlaycount > breakPoint)
                {
                    goalAmount = breakPoint;
                    break;
                }
            }
        }

        if (goalAmount < 1)
        {
            goalAmount = 1;
        }

        return (goalAmount, isRandom);
    }

    public static GuildRankingSettings SetGuildRankingSettings(GuildRankingSettings guildRankingSettings, string extraOptions)
    {
        var setGuildRankingSettings = guildRankingSettings;

        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return setGuildRankingSettings;
        }

        var playcounts = new[] { "p", "pc", "playcount", "plays" };
        if (Contains(extraOptions, playcounts))
        {
            guildRankingSettings.NewSearchValue = ContainsAndRemove(guildRankingSettings.NewSearchValue, playcounts);
            setGuildRankingSettings.OrderType = OrderType.Playcount;
        }

        var listenerCounts = new[] { "l", "lc", "listenercount", "listeners" };
        if (Contains(extraOptions, listenerCounts))
        {
            guildRankingSettings.NewSearchValue = ContainsAndRemove(guildRankingSettings.NewSearchValue, listenerCounts);
            setGuildRankingSettings.OrderType = OrderType.Listeners;
        }

        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            guildRankingSettings.NewSearchValue = null;
        }

        return setGuildRankingSettings;
    }

    public static FeaturedView SetFeaturedTypeView(string extraOptions)
    {
        var featuredView = FeaturedView.User;

        var global = new[] { "g", "global", "gw" };
        if (Contains(extraOptions, global))
        {
            featuredView = FeaturedView.Global;
        }
        var friends = new[] { "friends", "f" };
        if (Contains(extraOptions, friends))
        {
            featuredView = FeaturedView.Friends;
        }
        var guild = new[] { "server", "guild", "s" };
        if (Contains(extraOptions, guild))
        {
            featuredView = FeaturedView.Server;
        }

        return featuredView;
    }

    public static GuildRankingSettings TimeSettingsToGuildRankingSettings(GuildRankingSettings guildRankingSettings, TimeSettingsModel timeSettings)
    {
        guildRankingSettings.ChartTimePeriod = timeSettings.TimePeriod;
        guildRankingSettings.TimeDescription = timeSettings.Description;
        guildRankingSettings.EndDateTime = timeSettings.EndDateTime.GetValueOrDefault();
        guildRankingSettings.BillboardEndDateTime = timeSettings.BillboardEndDateTime.GetValueOrDefault();
        guildRankingSettings.BillboardTimeDescription = timeSettings.BillboardTimeDescription;
        guildRankingSettings.AmountOfDays = timeSettings.PlayDays.GetValueOrDefault();
        guildRankingSettings.AmountOfDaysWithBillboard = timeSettings.PlayDaysWithBillboard.GetValueOrDefault();
        guildRankingSettings.StartDateTime = timeSettings.StartDateTime.GetValueOrDefault(DateTime.UtcNow.AddDays(-guildRankingSettings.AmountOfDays));
        guildRankingSettings.BillboardStartDateTime = timeSettings.BillboardStartDateTime.GetValueOrDefault(DateTime.UtcNow.AddDays(-guildRankingSettings.AmountOfDaysWithBillboard));
        guildRankingSettings.NewSearchValue = timeSettings.NewSearchValue;

        return guildRankingSettings;
    }

    public static CrownViewType SetCrownViewSettings(string extraOptions)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return CrownViewType.Playcount;
        }

        if (extraOptions.Contains("p") || extraOptions.Contains("pc") || extraOptions.Contains("playcount") || extraOptions.Contains("plays"))
        {
            return CrownViewType.Playcount;
        }
        if (extraOptions.Contains("r") || extraOptions.Contains("rc") || extraOptions.Contains("recent") || extraOptions.Contains("new") || extraOptions.Contains("latest"))
        {
            return CrownViewType.Recent;
        }

        if (extraOptions.Contains("s") || extraOptions.Contains("stolen") || extraOptions.Contains("yoinked") ||
            extraOptions.Contains("yeeted"))
        {
            return CrownViewType.Stolen;
        }

        return CrownViewType.Playcount;
    }

    public static bool Contains(string extraOptions, string[] values)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return false;
        }

        var optionArray = extraOptions.Split(" ");

        foreach (var value in values)
        {
            foreach (var option in optionArray)
            {
                if (option.ToLower().Equals(value.ToLower()))
                {
                    return true;
                }
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
