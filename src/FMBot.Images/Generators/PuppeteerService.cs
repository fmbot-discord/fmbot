using System.Text;
using FMBot.Domain.Models;
using PuppeteerSharp;
using SkiaSharp;

namespace FMBot.Images.Generators;

public class PuppeteerService
{
    private Browser? _browser;
    private readonly Task _initializationTask;

    public PuppeteerService()
    {
        this._initializationTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        Console.WriteLine("Fetching browser");
        using var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        Console.WriteLine("Starting browser");
        this._browser = await Puppeteer.LaunchAsync(new LaunchOptions()
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox"
            }
        });
    }

    public async Task<SKBitmap> GetWorldArtistMap(List<TopCountry> artists)
    {
        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();

        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = 2754,
            Height = 1398
        });

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages", "world.html");

        var content = await File.ReadAllTextAsync(localPath);

        var groupedCountries = GetGroupedCountries(artists);

        var cssToAdd = new StringBuilder();
        foreach (var groupedCountry in groupedCountries)
        {
            foreach (var country in groupedCountry.CountryCodes)
            {
                cssToAdd.Append($".{country.ToLower()}{{fill:#4949ff; fill-opacity: {groupedCountry.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}}} ");
            }
        }

        content = content.Replace("{{customcss}}", cssToAdd.ToString());

        await page.SetContentAsync(content);

        await page.WaitForSelectorAsync("svg");

        var img = await page.ScreenshotDataAsync();

        var skImg = SKBitmap.FromImage(SKImage.FromEncodedData(img));
        AddTitleToChartImage(skImg, groupedCountries);

        return skImg;
    }

    private List<GroupedCountries> GetGroupedCountries(List<TopCountry> artists)
    {
        var list = new List<GroupedCountries>();

        var ceilings = new[] { 5, 10, 35, 80, 200, 500, 10000 };
        var groupings = artists.GroupBy(item => ceilings.FirstOrDefault(ceiling => ceiling >= item.Artists.Count));

        double alpha = 1;
        foreach (var artistGroup in groupings.OrderByDescending(o => o.Key))
        {
            list.Add(new GroupedCountries(artistGroup.Select(s => s.CountryCode).ToList(),
                artistGroup.Min(m => m.Artists.Count),
                artistGroup.Max(m => m.Artists.Count),
                alpha));
            alpha -= 0.13;
        }

        return list;
    }

    private record GroupedCountries(List<string> CountryCodes, int MinAmount, int MaxAmount, double Opacity);

    private void AddTitleToChartImage(SKBitmap chartImage, List<GroupedCountries> lines)
    {
        var textSize = 35;

        var typeface = SKTypeface.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "arial-unicode-ms.ttf"));

        using var textPaint = new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Color = SKColors.Black,
            Typeface = typeface
        };

        using var promoTextPaint = new SKPaint
        {
            TextSize = 16,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Color = SKColors.Black,
            Typeface = typeface
        };

        textPaint.TextSize = textSize;

        using var rectanglePaint = new SKPaint
        {
            TextAlign = SKTextAlign.Center,
            Color = new SKColor(244, 244, 244),
            IsAntialias = true,
        };

        using var bitmapCanvas = new SKCanvas(chartImage);

        var lineHeight = 65;

        var rectangleLeft = 100;
        var rectangleRight = 440;
        var rectangleTop = 700;
        var rectangleBottom = 1350;

        var backgroundRectangle = new SKRect(rectangleLeft, rectangleTop, rectangleRight, rectangleBottom - (lineHeight * (7 - lines.Count)));

        bitmapCanvas.DrawRoundRect(backgroundRectangle, 12, 12, rectanglePaint);

        bitmapCanvas.DrawText($"Artists per country", rectangleLeft + 170, rectangleTop + 58, textPaint);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            using var colorRectanglePaint = new SKPaint
            {
                TextAlign = SKTextAlign.Center,
                Color = new SKColor(73, 63, 255, (byte)(line.Opacity * 255)),
                IsAntialias = true
            };
            using var colorRectanglePaintBackground = new SKPaint
            {
                TextAlign = SKTextAlign.Center,
                Color = new SKColor(0, 0, 24),
                IsAntialias = true
            };
            var colorRectangle = new SKRect(rectangleLeft + 25, rectangleTop + 100 + (index * lineHeight), rectangleLeft + 90, rectangleTop + 132 + (index * lineHeight));
            bitmapCanvas.DrawRoundRect(colorRectangle, 8, 8, colorRectanglePaintBackground);
            bitmapCanvas.DrawRoundRect(colorRectangle, 8, 8, colorRectanglePaint);

            bitmapCanvas.DrawText($"{line.MinAmount} - {line.MaxAmount}", rectangleLeft + 200, rectangleTop + 129 + (index * 65), textPaint);
        }

        bitmapCanvas.DrawText($"{lines.SelectMany(s => s.CountryCodes).Count()} countries", rectangleLeft + 170, rectangleTop + 140 + ((lines.Count) * lineHeight), textPaint);
        bitmapCanvas.DrawText($"Generated by .fmbot", rectangleLeft + 170, rectangleTop + 170 + ((lines.Count) * lineHeight), promoTextPaint);
    }
}
