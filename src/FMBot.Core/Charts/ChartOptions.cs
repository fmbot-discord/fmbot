using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core.Charts;

public delegate string ChartTranslate(string key, params (string Name, string Value)[] args);

public delegate string ChartTranslateCount(string key, long count, params (string Name, string Value)[] args);

public static class ChartOptions
{
    public static List<string> SettingsLines(ChartSettings chartSettings, ChartTranslate translate,
        ChartTranslateCount translateCount)
    {
        var lines = new List<string>();

        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            lines.Add(translate("chart.filterReleaseYear",
                ("year", chartSettings.ReleaseYearFilter.Value.ToString())));
        }

        if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            lines.Add(translate("chart.filterReleaseDecade",
                ("decade", chartSettings.ReleaseDecadeFilter.Value.ToString())));
        }

        if (chartSettings.FilterSingles)
        {
            lines.Add(translate("chart.filterSingles"));
        }

        if (chartSettings.SkipWithoutImage)
        {
            lines.Add(chartSettings.ArtistChart
                ? translate("chart.skippingArtistsWithoutImages")
                : translate("chart.skippingAlbumsWithoutImages"));
        }

        if (chartSettings.SkipNsfw)
        {
            lines.Add(chartSettings.ArtistChart
                ? translate("chart.skippingNsfwArtists")
                : translate("chart.skippingNsfwAlbums"));
        }

        if (chartSettings.TitleSetting == TitleSetting.TitlesDisabled)
        {
            lines.Add(chartSettings.ArtistChart
                ? translate("chart.artistTitlesDisabled")
                : translate("chart.albumTitlesDisabled"));
        }

        if (chartSettings.FilteredArtist != null && !chartSettings.ArtistChart)
        {
            lines.Add(translate("chart.filterArtist",
                ("artist", DiscordStringExtensions.Sanitize(chartSettings.FilteredArtist.Name)),
                ("url", LastfmUrlExtensions.GetArtistUrl(chartSettings.FilteredArtist.Name))));
        }

        if (chartSettings.HasGenreFilter)
        {
            lines.Add(translateCount("chart.filterGenres",
                chartSettings.FilteredGenres.Count,
                ("genres", string.Join("**, **", chartSettings.FilteredGenres.Select(g => DiscordStringExtensions.Sanitize(g))))));
        }

        if (chartSettings.RainbowSortingEnabled)
        {
            lines.Add(translate("chart.rainbow"));
        }

        return lines;
    }

    public static (int? Year, int? Decade) ParseReleaseFilter(string releaseFilter)
    {
        var releaseFilterStr = releaseFilter?.Trim();
        if (string.IsNullOrWhiteSpace(releaseFilterStr))
        {
            return (null, null);
        }

        if (releaseFilterStr.EndsWith("s") &&
            int.TryParse(releaseFilterStr.TrimEnd('s'), out var decade) &&
            decade >= 1900 && decade % 10 == 0)
        {
            return (null, decade);
        }

        if (int.TryParse(releaseFilterStr, out var year) && year is >= 1900 and <= 2100)
        {
            return (year, null);
        }

        return (null, null);
    }

    public static ChartSettings FromEditOptions(bool artistChart, string size, string timePeriod,
        bool titles, bool plays, bool skip, bool sfw, bool rainbow, bool hideSingles, string releaseFilter,
        List<string> filteredGenres, Artist filteredArtist, string timeZone, Language language)
    {
        var titleSetting = !titles ? TitleSetting.TitlesDisabled
            : plays ? TitleSetting.TitlesWithPlays
            : TitleSetting.Titles;
        var skipWithoutImage = skip || rainbow;

        var (releaseYear, releaseDecade) = ParseReleaseFilter(releaseFilter);

        var hasFilters = releaseYear.HasValue || releaseDecade.HasValue || filteredArtist != null;
        var timeSettings = TimePeriodParser.GetTimePeriod(
            timePeriod,
            hasFilters ? TimePeriod.AllTime : TimePeriod.Weekly,
            timeZone: timeZone,
            language: language);

        var chartSettings = new ChartSettings
        {
            ArtistChart = artistChart,
            FilteredArtist = filteredArtist,
            TitleSetting = titleSetting,
            SkipWithoutImage = skipWithoutImage,
            SkipNsfw = sfw,
            RainbowSortingEnabled = rainbow,
            FilterSingles = hideSingles,
            TimeSettings = timeSettings,
            TimespanString = timeSettings.Description,
            TimespanUrlString = timeSettings.UrlParameter,
            ReleaseYearFilter = releaseYear,
            ReleaseDecadeFilter = releaseDecade,
            FilteredGenres = filteredGenres,
            CustomOptionsEnabled = titleSetting != TitleSetting.Titles || skipWithoutImage || sfw || rainbow ||
                                   hideSingles || filteredGenres.Count > 0
        };

        return ChartRenderer.GetDimensions(chartSettings, size).newChartSettings;
    }
}
