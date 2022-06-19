using System.ComponentModel.DataAnnotations;
using System.Text;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using PuppeteerSharp;
using SkiaSharp;

namespace FMBot.Images.Generators;

public class PuppeteerService
{
    private Browser? _browser;
    private readonly Task _initializationTask;
    private int _orderNr;

    public PuppeteerService()
    {
        this._orderNr = 1;
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
                "--no-sandbox",
                "--font-render-hinting=none"
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
                cssToAdd.Append($".{country.ToLower()}{{fill:#497dff; fill-opacity: {groupedCountry.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}}} ");
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

    public async Task<SKBitmap> GetReceipt(UserSettingsModel user, TopTrackList topTracks,
        TimeSettingsModel timeSettings, long? count)
    {
        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();

        const int amountOfTracks = 12;
        const int lineHeight = 20;

        var extraHeight = 0;
        foreach (var topTrack in topTracks.TopTracks)

        {
            var length = $"{topTrack.ArtistName} - {topTrack.TrackName}".Length;
            if (length > 36)
            {
                extraHeight += lineHeight;
            }
        }

        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = 500,
            Height = 870 + (topTracks.TotalAmount > 0 ? lineHeight : 0) + (user.UserType == UserType.Supporter ? lineHeight : 0) + extraHeight
        });

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages", "receipt.html");

        var content = await File.ReadAllTextAsync(localPath);

        var tracksToAdd = new StringBuilder();
        foreach (var topTrack in topTracks.TopTracks.Take(amountOfTracks))
        {
            tracksToAdd.Append("<tr>");
            tracksToAdd.Append("<td>");
            tracksToAdd.Append($"{topTrack.ArtistName} - {topTrack.TrackName}");
            tracksToAdd.Append("</td>");
            tracksToAdd.Append("<td class=\"align-right\">");
            tracksToAdd.Append($"{topTrack.UserPlaycount}");
            tracksToAdd.Append("</td>");
            tracksToAdd.Append("</tr>");
        }

        content = content.Replace("{{tracks}}", tracksToAdd.ToString());

        content = content.Replace("{{subtotal}}", topTracks.TopTracks.Take(amountOfTracks).Sum(s => s.UserPlaycount.GetValueOrDefault()).ToString());
        content = content.Replace("{{total-plays}}", count.GetValueOrDefault().ToString());

        if (topTracks.TotalAmount > 0)
        {
            var totalTracks = new StringBuilder();
            tracksToAdd.Append("<tr>");
            totalTracks.Append("<td class=\"min-width\"></td>");
            totalTracks.Append("<td>TOTAL TRACKS:</td>");
            totalTracks.Append($"<td class=\"align-right\">{topTracks.TotalAmount}</td>");
            tracksToAdd.Append("</tr>");

            content = content.Replace("{{total-tracks}}", totalTracks.ToString());
        }
        else
        {
            content = content.Replace("{{total-tracks}}", "");
        }

        content = content.Replace("{{order}}", this._orderNr.ToString());
        this._orderNr++;

        content = content.Replace("{{time-period}}", timeSettings.Description);
        content = content.Replace("{{date-generated}}", DateTime.UtcNow.ToLongDateString());
        content = content.Replace("{{lfm-username}}", user.UserNameLastFm);
        content = content.Replace("{{discord-username}}", user.DiscordUserName);
        content = content.Replace("{{auth-code}}", user.UserId.ToString());
        content = content.Replace("{{background-offset}}", new Random().Next(10, 1000).ToString());
        content = content.Replace("{{year}}", timeSettings.EndDateTime.HasValue ? timeSettings.EndDateTime.Value.Year.ToString() : DateTime.UtcNow.Year.ToString());

        content = content.Replace("{{thanks}}", user.UserType == UserType.Supporter ?
            "Thank you for being an .fmbot supporter - Dankjewel !" : "Thank you for visiting - Dankjewel !");

        await page.SetContentAsync(content);

        await page.WaitForSelectorAsync("table");

        var img = await page.ScreenshotDataAsync();

        return SKBitmap.FromImage(SKImage.FromEncodedData(img));
    }

    private List<GroupedCountries> GetGroupedCountries(List<TopCountry> artists)
    {
        var list = new List<GroupedCountries>();

        var ceilings = new[] { 5, 30, 80, 200, 500, 10000 };
        var groupings = artists.GroupBy(item => ceilings.FirstOrDefault(ceiling => ceiling >= item.Artists.Count));

        double alpha = 1;
        foreach (var artistGroup in groupings.OrderByDescending(o => o.Key))
        {
            list.Add(new GroupedCountries(artistGroup.Select(s => s.CountryCode).ToList(),
                artistGroup.Min(m => m.Artists.Count),
                artistGroup.Max(m => m.Artists.Count),
                alpha));
            alpha -= 0.16;
        }

        return list;
    }

    private record GroupedCountries(List<string> CountryCodes, int MinAmount, int MaxAmount, double Opacity);

    private void AddTitleToChartImage(SKBitmap chartImage, List<GroupedCountries> lines)
    {
        const int textSize = 35;

        var typeface = SKTypeface.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worksans-regular.otf"));

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
                Color = new SKColor(73, 125, 255, (byte)(line.Opacity * 255)),
                IsAntialias = true
            };
            using var colorRectanglePaintBackground = new SKPaint
            {
                TextAlign = SKTextAlign.Center,
                Color = new SKColor(0, 0, 15),
                IsAntialias = true
            };
            var colorRectangle = new SKRect(rectangleLeft + 25, rectangleTop + 100 + (index * lineHeight), rectangleLeft + 90, rectangleTop + 132 + (index * lineHeight));
            bitmapCanvas.DrawRoundRect(colorRectangle, 8, 8, colorRectanglePaintBackground);
            bitmapCanvas.DrawRoundRect(colorRectangle, 8, 8, colorRectanglePaint);

            var text = line.MinAmount == line.MaxAmount ? $"{line.MinAmount}" : $"{line.MinAmount} - {line.MaxAmount}";
            bitmapCanvas.DrawText(text, rectangleLeft + 200, rectangleTop + 129 + (index * 65), textPaint);
        }

        bitmapCanvas.DrawText($"{lines.SelectMany(s => s.CountryCodes).Count()} countries", rectangleLeft + 170, rectangleTop + 140 + ((lines.Count) * lineHeight), textPaint);
        bitmapCanvas.DrawText($"Generated by .fmbot", rectangleLeft + 170, rectangleTop + 170 + ((lines.Count) * lineHeight), promoTextPaint);
    }
}
