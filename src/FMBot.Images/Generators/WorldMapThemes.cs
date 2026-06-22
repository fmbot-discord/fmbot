using System.Collections.Generic;
using FMBot.Domain.Enums;
using SkiaSharp;

namespace FMBot.Images.Generators;

public record WorldMapTheme
{
    public string SvgBackground { get; init; }
    public string OceanFill { get; init; }
    public string LandFill { get; init; }
    public string LandStroke { get; init; }
    public string LandStrokeWidth { get; init; }
    public string[] CountryRamp { get; init; }
    public SKColor LegendBoxFill { get; init; }
    public SKColor LegendBoxBorder { get; init; }
    public SKColor LegendTitleColor { get; init; }
    public SKColor LegendSubtitleColor { get; init; }
    public SKColor LegendSwatchBorder { get; init; }
}

public static class WorldMapThemes
{
    private static readonly Dictionary<CountryChartTheme, WorldMapTheme> Themes = new()
    {
        [CountryChartTheme.Dark] = new WorldMapTheme
        {
            SvgBackground = "#000007",
            OceanFill = "#000007",
            LandFill = "#0a0a1a",
            LandStroke = "#cfd6e6",
            LandStrokeWidth = "1",
            CountryRamp = ["#dbeeff", "#8ec6ff", "#5491ff", "#3d5ef2", "#3239d2", "#282c88"],
            LegendBoxFill = new SKColor(13, 13, 34, 240),
            LegendBoxBorder = new SKColor(58, 69, 119),
            LegendTitleColor = new SKColor(255, 255, 255),
            LegendSubtitleColor = new SKColor(170, 180, 212),
            LegendSwatchBorder = new SKColor(58, 69, 119),
        },
        [CountryChartTheme.Light] = new WorldMapTheme
        {
            SvgBackground = "#e8f1fa",
            OceanFill = "#e8f1fa",
            LandFill = "#a7b0be",
            LandStroke = "#94a0b0",
            LandStrokeWidth = "1",
            CountryRamp = ["#062247", "#103760", "#1b4b78", "#256091", "#3074a9", "#3a89c2"],
            LegendBoxFill = new SKColor(255, 255, 255, 245),
            LegendBoxBorder = new SKColor(184, 194, 207),
            LegendTitleColor = new SKColor(26, 39, 51),
            LegendSubtitleColor = new SKColor(70, 85, 99),
            LegendSwatchBorder = new SKColor(125, 138, 153),
        },
        [CountryChartTheme.Ocean] = new WorldMapTheme
        {
            SvgBackground = "#0c2f4a",
            OceanFill = "#0c2f4a",
            LandFill = "#3d2a0f",
            LandStroke = "#8a6a3c",
            LandStrokeWidth = "1.1",
            CountryRamp = ["#bd0026", "#f03b20", "#fd8d3c", "#feb24c", "#fed976", "#ffffb2"],
            LegendBoxFill = new SKColor(42, 28, 10, 242),
            LegendBoxBorder = new SKColor(106, 79, 40),
            LegendTitleColor = new SKColor(247, 236, 214),
            LegendSubtitleColor = new SKColor(216, 196, 154),
            LegendSwatchBorder = new SKColor(122, 90, 44),
        },
        [CountryChartTheme.Synthwave] = new WorldMapTheme
        {
            SvgBackground = "#0c0420",
            OceanFill = "#0c0420",
            LandFill = "#190a2e",
            LandStroke = "#c83fb0",
            LandStrokeWidth = "1.1",
            CountryRamp = ["#88fbe8", "#46ccff", "#7b8cff", "#b24ff0", "#bf2a9c", "#a8205e"],
            LegendBoxFill = new SKColor(36, 17, 65, 242),
            LegendBoxBorder = new SKColor(106, 54, 160),
            LegendTitleColor = new SKColor(255, 156, 240),
            LegendSubtitleColor = new SKColor(207, 159, 255),
            LegendSwatchBorder = new SKColor(255, 123, 230),
        },
    };

    public static WorldMapTheme Get(CountryChartTheme theme)
    {
        return Themes.TryGetValue(theme, out var palette) ? palette : Themes[CountryChartTheme.Dark];
    }
}
