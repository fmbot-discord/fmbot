using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using Serilog;
using SkiaSharp;
using Color = System.Drawing.Color;

namespace FMBot.Bot.Services;

public class ChartService
{
    private const int DefaultChartSize = 3;

    private readonly CensorService _censorService;

    private readonly string _fontPath;
    private readonly string _altFontPath;
    private readonly string _loadingErrorImagePath;
    private readonly string _unknownImagePath;
    private readonly string _unknownArtistImagePath;
    private readonly string _censoredImagePath;

    private readonly HttpClient _client;

    public ChartService(CensorService censorService, HttpClient httpClient)
    {
        this._censorService = censorService;

        this._client = httpClient;

        try
        {
            this._fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "arial-unicode-ms.ttf");
            this._altFontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worksans-regular.otf");
            this._loadingErrorImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loading-error.png");
            this._unknownImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unknown.png");
            this._unknownArtistImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unknown-artist.png");
            this._censoredImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "censored.png");

            if (!File.Exists(this._fontPath))
            {
                Log.Information("Downloading chart files...");
                var wc = new System.Net.WebClient();
                wc.DownloadFile("https://fmbot.xyz/fonts/arial-unicode-ms.ttf", this._fontPath);
                wc.DownloadFile("https://fmbot.xyz/fonts/worksans-regular.otf", this._altFontPath);
                wc.DownloadFile("https://fmbot.xyz/img/bot/loading-error.png", this._loadingErrorImagePath);
                wc.DownloadFile("https://fmbot.xyz/img/bot/unknown.png", this._unknownImagePath);
                wc.DownloadFile("https://fmbot.xyz/img/bot/unknown-artist.png", this._unknownArtistImagePath);
                wc.DownloadFile("https://fmbot.xyz/img/bot/censored.png", this._censoredImagePath);
                wc.Dispose();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Something went wrong while downloading chart files");
        }
    }

    public async Task<SKImage> GenerateChartAsync(ChartSettings chart)
    {
        try
        {
            var largerImages = true;
            var chartImageHeight = 300;
            var chartImageWidth = 300;

            if (chart.ImagesNeeded > 40)
            {
                largerImages = false;
                chartImageHeight = 180;
                chartImageWidth = 180;
            }

            if (!chart.ArtistChart)
            {
                foreach (var album in chart.Albums)
                {
                    var censor = false;
                    var cacheEnabled = true;
                    var censorResult = await this._censorService.AlbumResult(album.AlbumName, album.ArtistName);

                    if (censorResult == CensorService.CensorResult.NotSafe)
                    {
                        cacheEnabled = false;
                        censor = true;
                    }

                    var nsfw = censorResult == CensorService.CensorResult.Nsfw;

                    SKBitmap chartImage;
                    var validImage = true;

                    var localPath = AlbumUrlToCacheFilePath(album.AlbumName, album.ArtistName);

                    if (localPath != null && File.Exists(localPath) && cacheEnabled)
                    {
                        chartImage = SKBitmap.Decode(localPath);
                        Statistics.LastfmCachedImageCalls.Inc();
                    }
                    else
                    {
                        if (album.AlbumCoverUrl != null)
                        {
                            var albumCoverUrl = album.AlbumCoverUrl;

                            if (albumCoverUrl.Contains("lastfm.freetls.fastly.net") && !albumCoverUrl.Contains("/300x300/"))
                            {
                                albumCoverUrl = albumCoverUrl.Replace("/770x0/", "/");
                                albumCoverUrl = albumCoverUrl.Replace("/i/u/", "/i/u/300x300/");
                            }

                            try
                            {
                                var bytes = await this._client.GetByteArrayAsync(albumCoverUrl);

                                Statistics.LastfmImageCalls.Inc();

                                await using var stream = new MemoryStream(bytes);
                                chartImage = SKBitmap.Decode(stream);
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error while loading image for generated chart", e);
                                chartImage = SKBitmap.Decode(this._loadingErrorImagePath);
                                validImage = false;
                            }

                            if (chartImage == null)
                            {
                                Log.Error("Error while loading image for generated chart (chartimg null)");
                                chartImage = SKBitmap.Decode(this._loadingErrorImagePath);
                                validImage = false;
                            }

                            if (validImage && cacheEnabled)
                            {
                                await SaveImageToCache(chartImage, localPath);
                            }
                        }
                        else
                        {
                            chartImage = SKBitmap.Decode(this._unknownImagePath);
                            validImage = false;
                        }
                    }

                    if (censor)
                    {
                        chartImage = SKBitmap.Decode(this._censoredImagePath);
                        validImage = false;
                    }

                    AddImageToChart(chart, chartImage, chartImageHeight, chartImageWidth, largerImages, validImage, album, nsfw: nsfw, censored: censor);
                }
            }
            else
            {
                foreach (var artist in chart.Artists)
                {
                    var censor = false;
                    var cacheEnabled = true;
                    var censorResult = await this._censorService.ArtistResult(artist.ArtistName);

                    if (censorResult == CensorService.CensorResult.NotSafe)
                    {
                        cacheEnabled = false;
                        censor = true;
                    }

                    var nsfw = censorResult == CensorService.CensorResult.Nsfw;

                    var encodedId = StringExtensions.ReplaceInvalidChars(
                        artist.ArtistUrl.Replace("https://www.last.fm/music/", ""));
                    var localArtistId = StringExtensions.TruncateLongString($"artist_{encodedId}", 60);

                    SKBitmap chartImage;
                    var validImage = true;

                    var fileName = localArtistId + ".png";
                    var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", fileName);

                    if (File.Exists(localPath) && cacheEnabled)
                    {
                        chartImage = SKBitmap.Decode(localPath);
                    }
                    else
                    {
                        if (artist.ArtistImageUrl != null)
                        {
                            try
                            {
                                var bytes = await this._client.GetByteArrayAsync(artist.ArtistImageUrl);
                                await using var stream = new MemoryStream(bytes);
                                chartImage = SKBitmap.Decode(stream);
                            }
                            catch (Exception e)
                            {
                                Log.Error("Error while loading image for generated artist chart", e);
                                chartImage = SKBitmap.Decode(this._loadingErrorImagePath);
                                validImage = false;
                            }

                            if (chartImage == null)
                            {
                                Log.Error("Error while loading image for generated artist chart (chartimg null)");
                                chartImage = SKBitmap.Decode(this._loadingErrorImagePath);
                                validImage = false;
                            }

                            if (validImage && cacheEnabled)
                            {
                                await SaveImageToCache(chartImage, localPath);
                            }
                        }
                        else
                        {
                            chartImage = SKBitmap.Decode(this._unknownArtistImagePath);
                            validImage = false;
                        }
                    }

                    if (censor)
                    {
                        chartImage = SKBitmap.Decode(this._censoredImagePath);
                        validImage = false;
                    }

                    AddImageToChart(chart, chartImage, chartImageHeight, chartImageWidth, largerImages, validImage, artist: artist, nsfw: nsfw, censored: censor);
                }
            }

            SKImage finalImage = null;

            using (var tempSurface = SKSurface.Create(new SKImageInfo(chart.ChartImages.First().Image.Width * chart.Width, chart.ChartImages.First().Image.Height * chart.Height)))
            {
                var canvas = tempSurface.Canvas;

                var offset = 0;
                var offsetTop = 0;
                var heightRow = 0;

                for (var i = 0; i < Math.Min(chart.ImagesNeeded, chart.ChartImages.Count); i++)
                {
                    IOrderedEnumerable<ChartImage> imageList;
                    if (chart.RainbowSortingEnabled)
                    {
                        imageList = chart.ChartImages
                            .OrderBy(o => o.PrimaryColor.Value.GetHue())
                            .ThenBy(o =>
                                (o.PrimaryColor.Value.R * 3 +
                                 o.PrimaryColor.Value.G * 2 +
                                 o.PrimaryColor.Value.B * 1));
                    }
                    else
                    {
                        imageList = chart.ChartImages.OrderBy(o => o.Index);
                    }

                    var chartImage = imageList
                        .Where(w => !chart.SkipNsfw || !w.Nsfw)
                        .Where(w => !chart.SkipWithoutImage || w.ValidImage)
                        .ElementAtOrDefault(i);

                    if (chartImage == null)
                    {
                        continue;
                    }

                    canvas.DrawBitmap(chartImage.Image, SKRect.Create(offset, offsetTop, chartImage.Image.Width, chartImage.Image.Height));


                    if (i == (chart.Width - 1) || i - (chart.Width) * heightRow == chart.Width - 1)
                    {
                        offsetTop += chartImage.Image.Height;
                        heightRow += 1;
                        offset = 0;
                    }
                    else
                    {
                        offset += chartImage.Image.Width;
                    }

                    if (chartImage.Nsfw)
                    {
                        chart.ContainsNsfw = true;
                    }
                    if (chartImage.Censored)
                    {
                        if (chart.CensoredItems.HasValue)
                        {
                            chart.CensoredItems++;
                        }
                        else
                        {
                            chart.CensoredItems = 1;
                        }
                    }
                }

                finalImage = tempSurface.Snapshot();
                tempSurface.Dispose();
            }

            return finalImage;
        }
        finally
        {
            foreach (var image in chart.ChartImages.Select(s => s.Image))
            {
                image.Dispose();
            }
        }
    }

    public static string AlbumUrlToCacheFilePath(string albumName, string artistName)
    {
        var encodedId = EncodeToBase64($"{StringExtensions.TruncateLongString(albumName, 80)}--{StringExtensions.TruncateLongString(artistName, 40)}");
        var localAlbumId = StringExtensions.TruncateLongString($"album_{encodedId}", 100);

        var fileName = localAlbumId + ".png";
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", fileName);
        return localPath;
    }

    private static string EncodeToBase64(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var value = Convert.ToBase64String(bytes);

        return StringExtensions.ReplaceInvalidChars(value);
    }

    public static async Task SaveImageToCache(SKBitmap chartImage, string localPath)
    {
        using var image = SKImage.FromBitmap(chartImage);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var stream = File.OpenWrite(localPath);
        data.SaveTo(stream);
    }

    public static async Task OverwriteCache(MemoryStream stream, string cacheFilePath)
    {
        stream.Position = 0;
        var chartImage = SKBitmap.Decode(stream);

        if (File.Exists(cacheFilePath))
        {
            File.Delete(cacheFilePath);
            await Task.Delay(100);
        }

        await SaveImageToCache(chartImage, cacheFilePath);
    }

    private void AddImageToChart(ChartSettings chart, SKBitmap chartImage, int chartImageHeight,
        int chartImageWidth,
        bool largerImages,
        bool validImage,
        TopAlbum album = null,
        TopArtist artist = null,
        bool nsfw = false,
        bool censored = false)
    {
        if (chartImage.Height != chartImageHeight || chartImage.Width != chartImageWidth)
        {
            using var surface = SKSurface.Create(new SKImageInfo(chartImageWidth, chartImageHeight));
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            var ratioBitmap = (float)chartImage.Width / (float)chartImage.Height;
            var ratioMax = (float)chartImageWidth / (float)chartImageHeight;

            var finalWidth = chartImageWidth;
            var finalHeight = chartImageHeight;
            if (ratioMax > ratioBitmap)
            {
                finalWidth = (int)((float)chartImageHeight * ratioBitmap);
            }
            else
            {
                finalHeight = (int)((float)chartImageWidth / ratioBitmap);
            }

            var leftOffset = finalWidth != chartImageWidth ? (chartImageWidth - finalWidth) / 2 : 0;
            var topOffset = finalHeight != chartImageHeight ? (chartImageHeight - finalHeight) / 2 : 0;

            surface.Canvas.DrawBitmap(chartImage, new SKRectI(leftOffset, topOffset, finalWidth + leftOffset, finalHeight + topOffset),
                paint);
            surface.Canvas.Flush();

            using var resizedImage = surface.Snapshot();
            chartImage = SKBitmap.FromImage(resizedImage);
        }

        switch (chart.TitleSetting)
        {
            case TitleSetting.Titles:
                AddTitleToChartImage(chartImage, largerImages, album, artist);
                break;
            case TitleSetting.ClassicTitles:
                AddClassicTitleToChartImage(chartImage, album);
                break;
            case TitleSetting.TitlesDisabled:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        Color? primaryColor = null;
        if (chart.RainbowSortingEnabled)
        {
            primaryColor = chartImage.GetAverageRgbColor();
        }

        var index = album != null ? chart.Albums.IndexOf(album) : chart.Artists.IndexOf(artist);

        chart.ChartImages.Add(new ChartImage(chartImage, index, validImage, primaryColor, nsfw, censored));
    }

    private void AddTitleToChartImage(SKBitmap chartImage, bool largerImages, TopAlbum album = null, TopArtist artist = null)
    {
        var textColor = chartImage.GetTextColor();
        var rectangleColor = textColor == SKColors.Black ? SKColors.White : SKColors.Black;

        var typeface = SKTypeface.FromFile(this._fontPath);

        var artistName = artist?.ArtistName ?? album?.ArtistName;
        var albumName = album?.AlbumName;

        var textSize = largerImages ? 17 : 12;

        using var textPaint = new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Color = textColor,
            Typeface = typeface
        };

        if (albumName != null && textPaint.MeasureText(albumName) > chartImage.Width ||
            textPaint.MeasureText(artistName) > chartImage.Width)
        {
            textPaint.TextSize = textSize - (largerImages ? 5 : 2);
        }

        using var rectanglePaint = new SKPaint
        {
            TextAlign = SKTextAlign.Center,
            Color = rectangleColor.WithAlpha(140),
            IsAntialias = true,
        };

        var artistBounds = new SKRect();
        var albumBounds = new SKRect();

        using var bitmapCanvas = new SKCanvas(chartImage);

        textPaint.MeasureText(artistName, ref artistBounds);
        textPaint.MeasureText(albumName, ref albumBounds);

        var rectangleLeft = (chartImage.Width - Math.Max(albumBounds.Width, artistBounds.Width)) / 2 - (largerImages ? 6 : 3);
        var rectangleRight = (chartImage.Width + Math.Max(albumBounds.Width, artistBounds.Width)) / 2 + (largerImages ? 6 : 3);
        var rectangleTop = albumName != null ? chartImage.Height - (largerImages ? 44 : 30) : chartImage.Height - (largerImages ? 23 : 16);
        var rectangleBottom = chartImage.Height - 1;

        var backgroundRectangle = new SKRect(rectangleLeft, rectangleTop, rectangleRight, rectangleBottom);

        bitmapCanvas.DrawRoundRect(backgroundRectangle, 4, 4, rectanglePaint);

        bitmapCanvas.DrawText(artistName, (float)chartImage.Width / 2, -artistBounds.Top + chartImage.Height - (albumName != null ? largerImages ? 39 : 26 : largerImages ? 20 : 13),
            textPaint);

        if (albumName != null)
        {
            bitmapCanvas.DrawText(albumName, (float)chartImage.Width / 2, -albumBounds.Top + chartImage.Height - (largerImages ? 20 : 13),
                textPaint);
        }
    }

    private static void AddClassicTitleToChartImage(SKBitmap chartImage, TopAlbum album)
    {
        var textColor = chartImage.GetTextColor();

        using var textPaint = new SKPaint
        {
            TextSize = 11,
            IsAntialias = true,
            Color = textColor,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        var artistBounds = new SKRect();
        var albumBounds = new SKRect();

        using var bitmapCanvas = new SKCanvas(chartImage);

        textPaint.MeasureText(album.ArtistName, ref artistBounds);
        textPaint.MeasureText(album.AlbumName, ref albumBounds);

        bitmapCanvas.DrawText(album.ArtistName, 4, 12, textPaint);
        bitmapCanvas.DrawText(album.AlbumName, 4, 22, textPaint);
    }

    public ChartSettings SetSettings(ChartSettings currentChartSettings, string[] extraOptions,
        UserSettingsModel userSettings, bool aoty = false, bool aotd = false)
    {
        var chartSettings = currentChartSettings;
        chartSettings.CustomOptionsEnabled = false;

        if (extraOptions.Contains("notitles") || extraOptions.Contains("nt"))
        {
            chartSettings.TitleSetting = TitleSetting.TitlesDisabled;
            chartSettings.CustomOptionsEnabled = true;
        }

        if (extraOptions.Contains("classictitles") || extraOptions.Contains("ct"))
        {
            chartSettings.TitleSetting = TitleSetting.ClassicTitles;
            chartSettings.CustomOptionsEnabled = true;
        }

        if (extraOptions.Contains("skipemptyimages") ||
            extraOptions.Contains("skipemptyalbums") ||
            extraOptions.Contains("skipalbums") ||
            extraOptions.Contains("skip") ||
            extraOptions.Contains("s"))
        {
            chartSettings.SkipWithoutImage = true;
            chartSettings.CustomOptionsEnabled = true;
        }
        if (extraOptions.Contains("sfw"))
        {
            chartSettings.SkipNsfw = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        if (extraOptions.Contains("rainbow") ||
            extraOptions.Contains("pride"))
        {
            chartSettings.RainbowSortingEnabled = true;
            chartSettings.SkipWithoutImage = true;
            chartSettings.CustomOptionsEnabled = true;
        }

        // chart size
        chartSettings.Width = DefaultChartSize;
        chartSettings.Height = DefaultChartSize;

        foreach (var option in extraOptions.Where(w => !string.IsNullOrWhiteSpace(w) && w.Length is >= 3 and <= 5))
        {
            GetDimensions(chartSettings, option);
        }

        var optionsAsString = "";
        if (extraOptions.Any())
        {
            optionsAsString = string.Join(" ", extraOptions);
        }

        if (aoty)
        {
            var year = SettingService.GetYear(optionsAsString);
            if (year != null)
            {
                chartSettings.ReleaseYearFilter = year;
                optionsAsString = optionsAsString.Replace($"{year}", "");
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
            foreach (var option in extraOptions)
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
                    }
                }
            }
            if (!aotdFound)
            {
                chartSettings.ReleaseDecadeFilter = (DateTime.UtcNow.Year / 10) * 10;
            }

            chartSettings.CustomOptionsEnabled = true;
        }

        foreach (var option in extraOptions)
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
                    }
                }
            }
        }

        var timeSettings = SettingService.GetTimePeriod(optionsAsString, aoty || aotd ? TimePeriod.AllTime : TimePeriod.Weekly, timeZone: userSettings.TimeZone);

        chartSettings.TimeSettings = timeSettings;
        chartSettings.TimespanString = timeSettings.Description;
        chartSettings.TimespanUrlString = timeSettings.UrlParameter;

        return chartSettings;
    }

    public static ChartSettings GetDimensions(ChartSettings chartSettings, string option)
    {
        var matchFound = Regex.IsMatch(option, "^([1-9]|[1-4][0-9]|50)x([1-9]|[1-9]|[1-4][0-9]|50)$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(300));
        if (matchFound)
        {
            var dimensions = option.ToLower().Split('x').Select(value =>
            {
                var size = int.TryParse(value, out var i) ? i : DefaultChartSize;
                return size;
            }).ToArray();

            chartSettings.Width = dimensions[0];
            chartSettings.Height = dimensions[1];
        }

        return chartSettings;
    }

    public static string AddSettingsToDescription(ChartSettings chartSettings, string embedDescription, string randomSupporter, string prfx)
    {
        var single = chartSettings.ArtistChart ? "Artist" : "Album";
        var multiple = chartSettings.ArtistChart ? "Artists" : "Albums";

        if (chartSettings.CustomOptionsEnabled)
        {
            embedDescription += "Chart options:\n";
        }
        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            embedDescription += $"- Filtering to albums released in {chartSettings.ReleaseYearFilter.Value}\n";
        }
        if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            embedDescription += $"- Filtering to albums released in the {chartSettings.ReleaseDecadeFilter.Value}s\n";
        }
        if (chartSettings.SkipWithoutImage)
        {
            embedDescription += $"- {multiple} without images skipped\n";
        }
        if (chartSettings.SkipNsfw)
        {
            embedDescription += $"- {multiple} with NSFW images skipped\n";
        }
        if (chartSettings.TitleSetting == TitleSetting.TitlesDisabled)
        {
            embedDescription += $"- {single} titles disabled\n";
        }
        if (chartSettings.TitleSetting == TitleSetting.ClassicTitles)
        {
            embedDescription += "- Classic titles enabled\n";
        }
        if (chartSettings.RainbowSortingEnabled)
        {
            embedDescription += "- Secret rainbow option enabled! (Not perfect but hey, it somewhat exists)\n";
        }

        var rnd = new Random();
        if (chartSettings.ImagesNeeded == 1 && rnd.Next(0, 3) == 1 && !chartSettings.ArtistChart)
        {
            embedDescription += $"*Linus Tech Tip: Use `{prfx}cover` if you just want to see an album cover.*\n";
        }

        if (!string.IsNullOrEmpty(randomSupporter))
        {
            embedDescription +=
                $"*This chart was brought to you by .fmbot supporter `{StringExtensions.Sanitize(randomSupporter)}`.*\n";
        }

        return embedDescription;
    }
}
