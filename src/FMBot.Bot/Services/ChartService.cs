using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Core.Charts;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services;

public class ChartService : ChartRenderer
{
    private readonly ArtistsService _artistsService;
    private readonly GenreService _genreService;

    public ChartService(Core.MusicCensorService censorService, HttpClient httpClient, ArtistsService artistsService,
        GenreService genreService) : base(censorService, httpClient)
    {
        this._artistsService = artistsService;
        this._genreService = genreService;
    }

    public async Task<ChartSettings> SetSettings(ChartSettings currentChartSettings, UserSettingsModel userSettings,
        bool aoty = false, bool aotd = false, Language language = Language.English)
    {
        var chartSettings = currentChartSettings;
        chartSettings.CustomOptionsEnabled = false;

        var optionsAsString = userSettings.NewSearchValue;
        var splitOptions = optionsAsString?.Split(' ') ?? [];
        var cleanedOptions = optionsAsString;

        var playsOptions = new[] { "playcounts", "plays", "pc" };
        if (SettingService.Contains(optionsAsString, playsOptions))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, playsOptions);
            chartSettings.TitleSetting = TitleSetting.TitlesWithPlays;
            chartSettings.CustomOptionsEnabled = true;
        }

        var noTitles = new[] { "notitles", "nt" };
        if (SettingService.Contains(optionsAsString, noTitles))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, noTitles);
            chartSettings.TitleSetting = TitleSetting.TitlesDisabled;
            chartSettings.CustomOptionsEnabled = true;
        }

        var skipOptions = new[] { "skipemptyimages", "skipemptyalbums", "skipalbums", "skip", "s" };
        if (SettingService.Contains(optionsAsString, skipOptions))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, skipOptions);
            chartSettings.SkipWithoutImage = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        var sfwOptions = new[] { "sfw" };
        if (SettingService.Contains(optionsAsString, sfwOptions))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, sfwOptions);
            chartSettings.SkipNsfw = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        var rainbowOptions = new[] { "rainbow", "pride" };
        if (SettingService.Contains(optionsAsString, rainbowOptions))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, rainbowOptions);
            chartSettings.RainbowSortingEnabled = true;
            chartSettings.SkipWithoutImage = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        var hideSinglesOptions = new[] { "ns", "nosingles", "hidesingles", "filtersingles" };
        if (SettingService.Contains(optionsAsString, hideSinglesOptions))
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, hideSinglesOptions);
            chartSettings.FilterSingles = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        chartSettings.Width = DefaultChartSize;
        chartSettings.Height = DefaultChartSize;

        var dimensionOptions = new List<string>();
        foreach (var option in splitOptions
                     .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length is >= 3 and <= 5))
        {
            var newDimensions = GetDimensions(chartSettings, option);

            if (newDimensions.Changed)
            {
                dimensionOptions.Add(option);
            }
        }

        if (dimensionOptions.Any())
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, dimensionOptions.ToArray());
        }

        if (aoty)
        {
            var year = SettingService.GetYear(cleanedOptions);
            if (year != null)
            {
                chartSettings.ReleaseYearFilter = year;
                cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, [year.ToString()]);
            }
            else
            {
                chartSettings.ReleaseYearFilter = DateTime.UtcNow.Year;
            }

            chartSettings.CustomOptionsEnabled = true;
        }

        if (aotd)
        {
            var aotdFound = false;
            var decadeOptions = new List<string>();

            foreach (var option in splitOptions)
            {
                var cleaned = option
                    .Replace("d:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("decade:", "", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('s')
                    .TrimEnd('S');

                if (int.TryParse(cleaned, out var year))
                {
                    if (year < 100)
                    {
                        year += year < 30 ? 2000 : 1900;
                    }

                    year = (year / 10) * 10;

                    if (year <= DateTime.UtcNow.Year && year >= 1900)
                    {
                        chartSettings.CustomOptionsEnabled = true;
                        chartSettings.ReleaseDecadeFilter = year;
                        aotdFound = true;
                        decadeOptions.Add(option);
                    }
                }
            }

            if (decadeOptions.Count != 0)
            {
                cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, decadeOptions.ToArray());
            }

            if (!aotdFound)
            {
                chartSettings.ReleaseDecadeFilter = (DateTime.UtcNow.Year / 10) * 10;
            }

            chartSettings.CustomOptionsEnabled = true;
        }

        var processedFilters = new List<string>();
        foreach (var option in splitOptions)
        {
            if (option.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                option.StartsWith("released:", StringComparison.OrdinalIgnoreCase))
            {
                var yearString = option
                    .Replace("r:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("released:", "", StringComparison.OrdinalIgnoreCase);

                var year = SettingService.GetYear(yearString);
                if (year != null)
                {
                    chartSettings.CustomOptionsEnabled = true;
                    chartSettings.ReleaseYearFilter = year;
                    aoty = true;
                    processedFilters.Add(option);
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
                        chartSettings.CustomOptionsEnabled = true;
                        chartSettings.ReleaseDecadeFilter = year;
                        aotd = true;
                        processedFilters.Add(option);
                    }
                }
            }
        }

        if (processedFilters.Any())
        {
            cleanedOptions = SettingService.ContainsAndRemove(cleanedOptions, processedFilters.ToArray());
        }

        var timeSettings = SettingService.GetTimePeriod(cleanedOptions,
            aoty || aotd ? TimePeriod.AllTime : TimePeriod.Weekly, timeZone: userSettings.TimeZone,
            language: language);

        if (!string.IsNullOrWhiteSpace(timeSettings.NewSearchValue))
        {
            var leftover = timeSettings.NewSearchValue.Trim();

            string genreValue = null;
            if (leftover.StartsWith("genre:", StringComparison.OrdinalIgnoreCase))
            {
                genreValue = leftover["genre:".Length..];
            }
            else if (leftover.StartsWith("g:", StringComparison.OrdinalIgnoreCase))
            {
                genreValue = leftover["g:".Length..];
            }

            if (genreValue != null)
            {
                var genreInputs = genreValue.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var resolvedGenres = await this._genreService.ResolveGenres(genreInputs);
                if (resolvedGenres.Count != 0)
                {
                    chartSettings.FilteredGenres = resolvedGenres;
                    chartSettings.CustomOptionsEnabled = true;
                }
            }
            else
            {
                var artist = await this._artistsService.GetArtistFromDatabase(leftover);
                if (artist != null)
                {
                    chartSettings.FilteredArtist = artist;
                    timeSettings = SettingService.GetTimePeriod(cleanedOptions, TimePeriod.AllTime,
                        timeZone: userSettings.TimeZone, language: language);
                }
            }
        }

        chartSettings.TimeSettings = timeSettings;
        chartSettings.TimespanString = timeSettings.Description;
        chartSettings.TimespanUrlString = timeSettings.UrlParameter;

        return chartSettings;
    }

    public static void AddSettingsToDescription(ChartSettings chartSettings, StringBuilder embedDescription,
        string randomSupporter, string prfx, Localizer localizer)
    {
        foreach (var line in ChartOptions.SettingsLines(chartSettings, localizer.Translate, localizer.TranslateCount))
        {
            embedDescription.AppendLine(line);
        }

        var rnd = new Random();
        if (chartSettings.ImagesNeeded == 1 && rnd.Next(0, 3) == 1 && !chartSettings.ArtistChart)
        {
            embedDescription.AppendLine(localizer.Translate("chart.coverTip", ("command", $"{prfx}cover")));
        }

        if (!string.IsNullOrEmpty(randomSupporter))
        {
            embedDescription.AppendLine(localizer.Translate("chart.broughtBy",
                ("name", StringExtensions.Sanitize(randomSupporter))));
        }
    }
}
