using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Ganss.Xss;
using PuppeteerSharp;
using Serilog;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
// ReSharper disable UsingStatementResourceInitialization

namespace FMBot.Images.Generators;

public class PuppeteerService : IDisposable
{
    private IBrowser _browser;
    private readonly Task _initializationTask;
    private int _orderNr;

    private static string cachedAppleMusicAuthToken;

    // Cache typeface to avoid repeated file loads and ensure proper disposal
    private readonly Lazy<SKTypeface> _cachedTypeface = new(() =>
        SKTypeface.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "bot", "sourcehansans-medium.otf")));


    public PuppeteerService()
    {
        this._orderNr = 1;
        this._initializationTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");

        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            Log.Information("Fetching puppeteer browser");
            await new BrowserFetcher().DownloadAsync();
            executablePath = null;
        }

        Log.Information("Starting puppeteer browser");
        this._browser = await Puppeteer.LaunchAsync(new LaunchOptions()
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args = new[]
            {
                "--no-sandbox",
                "--font-render-hinting=none"
            }
        });
    }

    public async Task<SKBitmap> GetWorldArtistMap(IEnumerable<TopCountry> artists)
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
                cssToAdd.Append(
                    $".{country.ToLower()}{{fill:#497dff; fill-opacity: {groupedCountry.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}}} ");
            }
        }

        content = content.Replace("{{customcss}}", cssToAdd.ToString());

        await page.SetContentAsync(content);

        await page.WaitForSelectorAsync("svg");

        var img = await page.ScreenshotDataAsync();

        using var skImage = SKImage.FromEncodedData(img);
        var skImg = SKBitmap.FromImage(skImage);
        AddCountryListToMap(skImg, groupedCountries);

        return skImg;
    }

    public async Task<SKBitmap> GetWhoKnows(string type, string location, string imageUrl, string title,
        IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId,
        PrivacyLevel minPrivacyLevel, NumberFormat numberFormat, UserCrown userCrown = null, string crownText = null,
        bool hidePrivateUsers = false)
    {
        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();

        var extraHeight = 0;
        if (title.Length > 38)
        {
            var lines = (int)(title.Length / 38);

            extraHeight += (lines) * 48;
        }

        if (crownText != null)
        {
            extraHeight += 96;
        }

        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = 1275,
            Height = 888 + extraHeight
        });

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages", "whoknows.html");
        var content = await File.ReadAllTextAsync(localPath);

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.Add("b");

        content = content.Replace("{{type}}", type);
        content = content.Replace("{{location}}", sanitizer.Sanitize(location));

        content = imageUrl != null
            ? content.Replace("{{image-url}}", imageUrl)
            : content.Replace("{{hide-img}}", "hidden");

        content = content.Replace("{{title}}", sanitizer.Sanitize(title));

        if (crownText != null)
        {
            content = content.Replace("{{crown-text}}", sanitizer.Sanitize(crownText));
        }
        else
        {
            content = content.Replace("{{crown-hide}}", "hidden");
        }

        var whoKnowsCount = whoKnowsObjects.Count;
        if (whoKnowsCount > 10)
        {
            whoKnowsCount = 10;
        }

        var usersToShow = whoKnowsObjects
            .OrderByDescending(o => o.Playcount)
            .ToList();

        var indexNumber = 1;
        var timesNameAdded = 0;
        var requestedUserAdded = false;
        var addedUsers = new List<int>();

        var userList = new StringBuilder();
        var requestedUser = whoKnowsObjects.FirstOrDefault(f => f.UserId == requestedUserId);

        for (var index = 0; timesNameAdded < whoKnowsCount; index++)
        {
            if (index >= usersToShow.Count)
            {
                break;
            }

            var user = usersToShow[index];

            if (addedUsers.Any(a => a.Equals(user.UserId)))
            {
                continue;
            }

            string name;
            if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
            {
                name = "Private user";
                if (hidePrivateUsers)
                {
                    indexNumber += 1;
                    continue;
                }
            }
            else
            {
                name = Name(user, sanitizer);
            }

            var positionCounter = $"{indexNumber}.";
            if (userCrown != null && userCrown.UserId == user.UserId)
            {
                positionCounter = "👑 ";
            }

            userList.Append(GetWhoKnowsLine(positionCounter,
                name, user.Playcount, numberFormat, user.UserId == requestedUserId));

            indexNumber += 1;
            timesNameAdded += 1;

            addedUsers.Add(user.UserId);

            if (user.UserId == requestedUserId)
            {
                requestedUserAdded = true;
            }

            if (!requestedUserAdded && requestedUser != null && indexNumber == 10)
            {
                break;
            }
        }

        if (!requestedUserAdded)
        {
            if (requestedUser != null)
            {
                var name = Name(requestedUser, sanitizer);
                var position = whoKnowsObjects.IndexOf(requestedUser) + 1;

                content = position switch
                {
                    > 100 and < 1000 => content.Replace("{{num-width}}", "66"),
                    > 1000 and < 10000 => content.Replace("{{num-width}}", "78"),
                    > 10000 => content.Replace("{{num-width}}", "90"),
                    _ => content.Replace("{{num-width}}", "48")
                };

                userList.Append(GetWhoKnowsLine($"{position}.",
                    name, requestedUser.Playcount, numberFormat, true));
            }
        }

        if (whoKnowsObjects.Any())
        {
            content = content.Replace("{{users}}", userList.ToString());

            content = content.Replace("{{listeners}}",
                whoKnowsObjects.Count(w => w.Playcount > 0).Format(numberFormat).ToString());
            content = content.Replace("{{plays}}",
                whoKnowsObjects.Sum(a => a.Playcount).Format(numberFormat).ToString());
            content = content.Replace("{{average}}",
                ((int)whoKnowsObjects.Average(a => a.Playcount)).Format(numberFormat).ToString());
        }
        else
        {
            content = content.Replace("{{users}}", "No results.");

            content = content.Replace("{{listeners}}", "0");
            content = content.Replace("{{plays}}", "0");
            content = content.Replace("{{average}}", "0");
        }


        await page.SetContentAsync(content);
        await page.WaitForSelectorAsync(".result-list");

        var img = await page.ScreenshotDataAsync();
        using var skImage = SKImage.FromEncodedData(img);
        return SKBitmap.FromImage(skImage);
    }

    private static string GetWhoKnowsLine(string position, string name, int plays, NumberFormat numberFormat,
        bool self = false)
    {
        name = name.Length > 18 ? $"{name[..17]}.." : name;
        var cssClass = self ? "num own-num" : "num";

        name = self ? $"<b>{name}</b>" : name;

        return $"""
                <li>
                    <div class="{cssClass}">{position}</div> {name} <span class="float-right">{plays.Format(numberFormat)}</span>
                </li>
                """;
    }

    private static string Name(WhoKnowsObjectWithUser user, HtmlSanitizer sanitizer)
    {
        var discordName = user.DiscordName;

        if (string.IsNullOrWhiteSpace(discordName))
        {
            discordName = user.LastFMUsername;
        }

        var nameWithLink = $"\u2066{sanitizer.Sanitize(discordName)}\u2069";

        return nameWithLink;
    }

    public async Task<SKBitmap> GetTopList(string name, string title, string type, string time, long totalDifferent,
        long totalPlays, string imageUrl, IList<TopListObject> topListObjects, NumberFormat numberFormat)
    {
        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();

        var extraHeight = 0;
        if (title.Length > 38)
        {
            var lines = (int)(title.Length / 38);

            extraHeight += (lines) * 32;
        }

        var subNamesEnabled = topListObjects.Any(a => a.SubName != null);

        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = 650,
            Height = 810 + extraHeight + (subNamesEnabled ? 125 : 0),
        });

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages", "top.html");
        var content = await File.ReadAllTextAsync(localPath);

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.Add("b");

        content = content.Replace("{{name}}", sanitizer.Sanitize(name) + "'s");
        content = content.Replace("{{title}}", sanitizer.Sanitize(title));
        content = content.Replace("{{time}}", $"{sanitizer.Sanitize(time)} · Playcounts");
        content = content.Replace("{{type}}", sanitizer.Sanitize(type));

        content = content.Replace("{{totalDifferent}}", totalDifferent.Format(numberFormat));
        content = content.Replace("{{totalPlays}}", totalPlays.Format(numberFormat));

        content = imageUrl != null
            ? content.Replace("{{image-url}}", imageUrl)
            : content.Replace("{{hide-img}}", "hidden");

        if (totalDifferent == 0)
        {
            content = content.Replace("{{diffCount}}", "hidden");
        }

        var topList = topListObjects
            .OrderByDescending(o => o.Playcount)
            .ToList();

        var indexNumber = 1;
        var timesNameAdded = 0;

        var userList = new StringBuilder();

        for (var index = 0; timesNameAdded < (subNamesEnabled ? 8 : 10); index++)
        {
            if (index >= topList.Count)
            {
                break;
            }

            var topItem = topList[index];


            var positionCounter = $"{indexNumber}.";

            if (subNamesEnabled)
            {
                userList.Append(GetTopLineWithSub(positionCounter, topItem.Name, topItem.SubName, topItem.Playcount,
                    sanitizer, numberFormat));
            }
            else
            {
                userList.Append(GetTopLine(positionCounter, topItem.Name, topItem.Playcount, sanitizer, numberFormat));
            }

            indexNumber += 1;
            timesNameAdded += 1;
        }

        var topPlaycount = topListObjects.First().Playcount;
        content = topPlaycount switch
        {
            > 100 and < 1000 => content.Replace("{{num-width}}", "44"),
            > 1000 and < 10000 => content.Replace("{{num-width}}", "52"),
            > 10000 => content.Replace("{{num-width}}", "60"),
            _ => content.Replace("{{num-width}}", "32")
        };

        if (topListObjects.Any())
        {
            content = content.Replace("{{users}}", userList.ToString());
        }
        else
        {
            content = content.Replace("{{users}}", "No results.");
        }

        await page.SetContentAsync(content);
        await page.WaitForSelectorAsync(".result-list");

        var img = await page.ScreenshotDataAsync();
        using var skImage = SKImage.FromEncodedData(img);
        return SKBitmap.FromImage(skImage);
    }

    private static string GetTopLine(string position, string name, long plays, HtmlSanitizer htmlSanitizer,
        NumberFormat numberFormat)
    {
        name = htmlSanitizer.Sanitize(name);
        name = name.Length > 28 ? $"{name[..27]}.." : name;

        return $"""
                <li>
                    <div class="num">{position}</div> {name} <span class="float-right">{plays.Format(numberFormat)}</span>
                </li>
                """;
    }

    private static string GetTopLineWithSub(string position, string name, string sub, long plays,
        HtmlSanitizer htmlSanitizer, NumberFormat numberFormat)
    {
        name = htmlSanitizer.Sanitize(name);
        name = name.Length > 28 ? $"{name[..27]}.." : name;

        sub = htmlSanitizer.Sanitize(sub);
        sub = sub.Length > 36 ? $"{sub[..35]}.." : sub;

        return $"""
                <li class="flex-wrap">
                    <div class="num" style="padding-top:12px;margin-left:-8px !important;">{position}</div>
                    <div>
                        <div>{name} <span class="float-right" style="padding-top:12px;">{plays.Format(numberFormat)}</span></div>
                        <div class="sub">{sub}</div>
                    </div>
                </li>
                """;
    }

    public async Task<SKBitmap> GetReceipt(UserSettingsModel user, TopTrackList topTracks,
        TimeSettingsModel timeSettings, long? count)
    {
        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();

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
            Height = 870 + (topTracks.TotalAmount > 0 ? lineHeight : 0) +
                     (user.UserType == UserType.Supporter ? lineHeight : 0) + extraHeight
        });

        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages", "receipt.html");
        var content = await File.ReadAllTextAsync(localPath);

        var tracksToAdd = new StringBuilder();
        foreach (var topTrack in topTracks.TopTracks.Take(amountOfTracks))
        {
            tracksToAdd.Append("<tr>");
            tracksToAdd.Append("<td>");
            tracksToAdd.Append($"{sanitizer.Sanitize(topTrack.ArtistName)} - {sanitizer.Sanitize(topTrack.TrackName)}");
            tracksToAdd.Append("</td>");
            tracksToAdd.Append("<td class=\"align-right\">");
            tracksToAdd.Append($"{topTrack.UserPlaycount}");
            tracksToAdd.Append("</td>");
            tracksToAdd.Append("</tr>");
        }

        content = content.Replace("{{tracks}}", tracksToAdd.ToString());

        content = content.Replace("{{subtotal}}",
            topTracks.TopTracks.Take(amountOfTracks).Sum(s => s.UserPlaycount.GetValueOrDefault()).ToString());
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

        content = content.Replace("{{time-period}}", sanitizer.Sanitize(timeSettings.Description));
        content = content.Replace("{{date-generated}}", DateTime.UtcNow.ToLongDateString());
        content = content.Replace("{{lfm-username}}", sanitizer.Sanitize(user.UserNameLastFm));
        content = content.Replace("{{discord-username}}", sanitizer.Sanitize(user.DisplayName));
        content = content.Replace("{{auth-code}}", user.UserId.ToString());
        content = content.Replace("{{background-offset}}", new Random().Next(10, 1000).ToString());
        content = content.Replace("{{year}}",
            timeSettings.EndDateTime.HasValue
                ? timeSettings.EndDateTime.Value.Year.ToString()
                : DateTime.UtcNow.Year.ToString());

        content = content.Replace("{{thanks}}",
            user.UserType == UserType.Supporter
                ? "Thank you for being an .fmbot supporter - Dankjewel !"
                : "Thank you for visiting - Dankjewel !");

        await page.SetContentAsync(content);
        await page.WaitForSelectorAsync("table");

        var img = await page.ScreenshotDataAsync();
        using var skImage = SKImage.FromEncodedData(img);
        return SKBitmap.FromImage(skImage);
    }

    private static List<GroupedCountries> GetGroupedCountries(IEnumerable<TopCountry> artists)
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

    private void AddCountryListToMap(SKBitmap chartImage, List<GroupedCountries> lines)
    {
        var typeface = _cachedTypeface.Value;

        using var titleFont = new SKFont(typeface, 42);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill
        };

        using var subtitleFont = new SKFont(typeface, 28);
        using var subtitlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(220, 220, 220), // Slightly muted white
            Style = SKPaintStyle.Fill
        };

        using var legendFont = new SKFont(typeface, 28);
        using var legendTextPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill
        };

        using var legendStrokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            Color = new SKColor(0, 0, 0, 180) // Semi-transparent black stroke
        };

        using var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(20, 20, 30, 240), // Dark with transparency
            Style = SKPaintStyle.Fill
        };

        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(80, 80, 100, 200)
        };

        using var bitmapCanvas = new SKCanvas(chartImage);
        using var shaper = new SKShaper(typeface);

        const int padding = 40;
        const int legendItemHeight = 55;
        const int colorBoxSize = 40;
        const int colorBoxMargin = 20;

        const int legendWidth = 400;
        var legendHeight = 120 + (lines.Count * legendItemHeight) + 80; // Header + items + footer

        const int legendLeft = 80;
        var legendTop = chartImage.Height - legendHeight - 80;

        var backgroundRect = new SKRect(legendLeft - padding, legendTop - padding,
            legendLeft + legendWidth + padding, legendTop + legendHeight + padding);
        bitmapCanvas.DrawRoundRect(backgroundRect, 20, 20, backgroundPaint);
        bitmapCanvas.DrawRoundRect(backgroundRect, 20, 20, borderPaint);

        var titleY = legendTop + 50;
        bitmapCanvas.DrawShapedText(shaper, "Artists per country",
            legendLeft + legendWidth / 2, titleY,
            SKTextAlign.Center, titleFont, titlePaint);

        var currentY = titleY + 80;

        foreach (var line in lines)
        {
            using var colorFillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(73, 125, 255, (byte)(line.Opacity * 255)),
                Style = SKPaintStyle.Fill
            };

            using var colorBorderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                Color = SKColors.White
            };

            var colorRect = new SKRect(legendLeft + colorBoxMargin, currentY - colorBoxSize + 12,
                legendLeft + colorBoxMargin + colorBoxSize, currentY + 12);

            bitmapCanvas.DrawRoundRect(colorRect, 8, 8, colorFillPaint);
            bitmapCanvas.DrawRoundRect(colorRect, 8, 8, colorBorderPaint);

            var text = line.MinAmount == line.MaxAmount ? $"{line.MinAmount}" : $"{line.MaxAmount} - {line.MinAmount}";
            const int textX = legendLeft + colorBoxMargin + colorBoxSize + 30;

            bitmapCanvas.DrawShapedText(shaper, text, textX, currentY,
                SKTextAlign.Left, legendFont, legendStrokePaint);
            bitmapCanvas.DrawShapedText(shaper, text, textX, currentY,
                SKTextAlign.Left, legendFont, legendTextPaint);

            currentY += legendItemHeight;
        }

        var totalCountries = lines.SelectMany(s => s.CountryCodes).Count();
        var footerY = currentY + 40;

        bitmapCanvas.DrawShapedText(shaper, $"{totalCountries} countries",
            legendLeft + legendWidth / 2, footerY,
            SKTextAlign.Center, subtitleFont, subtitlePaint);

        bitmapCanvas.DrawShapedText(shaper, "Generated by .fmbot",
            legendLeft + legendWidth / 2, footerY + 35,
            SKTextAlign.Center, subtitleFont, subtitlePaint);
    }

    private const int ImageWidth = 1241;
    private const int ImageHeight = 2003;
    private const int FirstArtistY = 525;
    private const int LastArtistY = ImageHeight - 190;
    private const int SectionHeight = 165;
    private const int TextColorChangeY = 935;
    private const int TotalSections = 8;

    private record ArtistNamePosition(int Order, float X, float Y);

    private List<ArtistNamePosition> GenerateArtistNamePositions(float sectionY, int sectionArtistsCount)
    {
        float margin = 240;
        var topY = sectionY + 35;
        var middleY = sectionY + SectionHeight / 2;
        var bottomY = sectionY + SectionHeight - 45;
        return sectionArtistsCount switch
        {
            1 => [new ArtistNamePosition(3, ImageWidth / 2, middleY),],
            2 =>
            [
                new ArtistNamePosition(2, ImageWidth - margin - 100, middleY),
                new ArtistNamePosition(4, margin + 100, middleY),
            ],
            3 =>
            [
                new ArtistNamePosition(3, ImageWidth / 2, middleY + 20),
                new ArtistNamePosition(2, ImageWidth - margin, topY + 15),
                new ArtistNamePosition(4, margin, topY + 15),
            ],
            _ =>
            [
                new ArtistNamePosition(3, ImageWidth / 2, middleY),
                new ArtistNamePosition(1, margin, topY),
                new ArtistNamePosition(2, ImageWidth - margin, topY),
                new ArtistNamePosition(4, margin, bottomY),
                new ArtistNamePosition(5, ImageWidth - margin, bottomY)
            ]
        };
    }


    public void CreatePopularityIcebergImage(SKBitmap chartImage, string username, string timePeriod,
        List<ArtistPopularity> artists)
    {
        var typeface = _cachedTypeface.Value;

        using var bitmapCanvas = new SKCanvas(chartImage);

        using var titleFont = new SKFont(typeface, 90);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        bitmapCanvas.DrawShapedText($"{username}'s", 590, 198, SKTextAlign.Center, titleFont, titlePaint);

        titleFont.Size = 60;
        bitmapCanvas.DrawShapedText($"iceberg", 830, 262, SKTextAlign.Center, titleFont, titlePaint);

        titleFont.Size = 42;
        bitmapCanvas.DrawShapedText(timePeriod, 980, 415, SKTextAlign.Center, titleFont, titlePaint);

        using var artistFont = new SKFont(typeface, 38);
        using var artistTextPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        using var artistStrokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10,
            Color = SKColors.White
        };

        const float sectionRange = 100f / TotalSections;
        const float totalAvailableHeight = LastArtistY - FirstArtistY;

        using var shaper = new SKShaper(typeface);

        for (var section = 0; section < TotalSections; section++)
        {
            var maxPopularity = 100 - (section * sectionRange);
            var minPopularity = maxPopularity - sectionRange;

            var minPlaycount = artists.Count(c => c.Playcount >= 15) > 100 ? 15 : 5;

            var sectionArtists = artists
                .Where(a =>
                    a.Playcount >= minPlaycount &&
                    a.Popularity > minPopularity && a.Popularity <= maxPopularity)
                .OrderByDescending(a => a.Playcount)
                .Take(5)
                .OrderByDescending(a => a.Name.Length)
                .ToList();

            var sectionY = FirstArtistY + (section * (totalAvailableHeight / TotalSections));
            var positions = GenerateArtistNamePositions(sectionY, sectionArtists.Count);

            for (var i = 0; i < sectionArtists.Count; i++)
            {
                var position = positions[i];
                var isWhiteText = position.Y >= TextColorChangeY;

                if (isWhiteText)
                {
                    artistTextPaint.Color = SKColors.White;
                    artistStrokePaint.Color = SKColor.Parse("#001B3A");

                    bitmapCanvas.DrawShapedText(shaper, sectionArtists[i].Name, position.X, position.Y,
                        SKTextAlign.Center, artistFont, artistStrokePaint);
                }

                bitmapCanvas.DrawShapedText(shaper, sectionArtists[i].Name, position.X, position.Y,
                    SKTextAlign.Center, artistFont, artistTextPaint);
            }
        }
    }

    public async Task<string> GetAppleToken()
    {
        if (!string.IsNullOrEmpty(cachedAppleMusicAuthToken))
        {
            return cachedAppleMusicAuthToken;
        }

        await this._initializationTask;

        await using var page = await this._browser.NewPageAsync();
        await page.GoToAsync("https://music.apple.com/us/album/the-tortured-poets-department/1736268215");

        var scriptElement = await page.QuerySelectorAsync("script[type='module']");
        if (scriptElement == null)
        {
            Log.Information("Failed to find Apple script with auth token.");
            return null;
        }

        var scriptSrc = await page.EvaluateFunctionAsync<string>("(element) => element.src", scriptElement);
        await page.GoToAsync(scriptSrc);

        var pageContent = await page.GetContentAsync();

        var match = Regex.Match(pageContent, "(?<=\")eyJhbGciOiJ.+?(?=\")");
        if (!match.Success)
        {
            Log.Information("Failed to find Apple auth token from script.");
            return null;
        }

        cachedAppleMusicAuthToken = match.Value;
        return $"Bearer {cachedAppleMusicAuthToken}";
    }

    public void Dispose()
    {
        if (_cachedTypeface.IsValueCreated)
        {
            _cachedTypeface.Value.Dispose();
        }

        _browser?.Dispose();
        GC.SuppressFinalize(this);
    }
}
