using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using Serilog;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Color = System.Drawing.Color;

namespace FMBot.Bot.Services;

public class ChartService
{
    private const int DefaultChartSize = 3;

    private readonly CensorService _censorService;
    private readonly ArtistsService _artistsService;

    private readonly string _fontPath;
    private readonly string _workSansFontPath;
    private readonly string _loadingErrorImagePath;
    private readonly string _unknownImagePath;
    private readonly string _unknownArtistImagePath;
    private readonly string _censoredImagePath;

    private readonly HttpClient _client;

    public ChartService(CensorService censorService, HttpClient httpClient, ArtistsService artistsService)
    {
        this._censorService = censorService;
        this._client = httpClient;
        this._artistsService = artistsService;

        this._fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sourcehansans-medium.otf");
        this._workSansFontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worksans-regular.otf");
        this._loadingErrorImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loading-error.png");
        this._unknownImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unknown.png");
        this._unknownArtistImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unknown-artist.png");
        this._censoredImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "censored.png");
    }

    public async Task DownloadChartFilesAsync()
    {
        if (File.Exists(this._fontPath))
        {
            Log.Information("Chart files already exist, not downloading them again");
            return;
        }

        var files = new Dictionary<string, string>
        {
            { "https://fm.bot/fonts/sourcehansans-medium.otf", this._fontPath },
            { "https://fm.bot/fonts/worksans-regular.otf", this._workSansFontPath },
            { "https://fm.bot/img/bot/loading-error.png", this._loadingErrorImagePath },
            { "https://fm.bot/img/bot/unknown.png", this._unknownImagePath },
            { "https://fm.bot/img/bot/unknown-artist.png", this._unknownArtistImagePath },
            { "https://fm.bot/img/bot/censored.png", this._censoredImagePath }
        };

        try
        {
            foreach (var file in files)
            {
                await DownloadFileAsync(file.Key, file.Value);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while downloading chart files");
        }
    }

    private async Task DownloadFileAsync(string url, string filePath)
    {
        using var response = await this._client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
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

                            if (albumCoverUrl.Contains("lastfm.freetls.fastly.net") &&
                                !albumCoverUrl.Contains("/300x300/"))
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

                    var index = chart.Albums.IndexOf(album);
                    chart.FileDescription.Append($"#{index + 1} {album.AlbumName} by {album.ArtistName}, ");
                    AddImageToChart(chart,
                        chartImage,
                        index,
                        chartImageHeight,
                        chartImageWidth,
                        largerImages,
                        validImage,
                        topName: chart.FilteredArtist == null ? album.ArtistName : album.AlbumName,
                        bottomName: chart.FilteredArtist == null ? album.AlbumName : null,
                        nsfw: nsfw,
                        censored: censor);
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

                    var index = chart.Artists.IndexOf(artist);
                    chart.FileDescription.Append($"#{index + 1} {artist.ArtistName}, ");
                    AddImageToChart(chart,
                        chartImage,
                        index,
                        chartImageHeight,
                        chartImageWidth,
                        largerImages,
                        validImage,
                        topName: artist.ArtistName,
                        nsfw: nsfw,
                        censored: censor);
                }
            }

            SKImage finalImage = null;

            using var tempSurface = SKSurface.Create(new SKImageInfo(
                chart.ChartImages.First().Image.Width * chart.Width,
                chart.ChartImages.First().Image.Height * chart.Height));
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

                canvas.DrawBitmap(chartImage.Image,
                    SKRect.Create(offset, offsetTop, chartImage.Image.Width, chartImage.Image.Height));


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

    public static string AlbumUrlToCacheFilePath(string albumName, string artistName, string extension = ".png")
    {
        var encodedId =
            EncodeToBase64(
                $"{StringExtensions.TruncateLongString(albumName, 80)}--{StringExtensions.TruncateLongString(artistName, 40)}");
        var localAlbumId = StringExtensions.TruncateLongString($"album_{encodedId}", 100);

        var fileName = localAlbumId + extension;
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

    public static async Task OverwriteCache(Stream stream, string cacheFilePath,
        SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        stream.Position = 0;

        if (File.Exists(cacheFilePath))
        {
            File.Delete(cacheFilePath);
            await Task.Delay(100);
        }

        if (format == SKEncodedImageFormat.Png)
        {
            var chartImage = SKBitmap.Decode(stream);
            await SaveImageToCache(chartImage, cacheFilePath);
        }
        else
        {
            await SaveStreamToCache(stream, cacheFilePath);
        }
    }

    private static async Task SaveStreamToCache(Stream stream, string localPath)
    {
        stream.Position = 0; // Ensure the stream is at the beginning
        await using var fileStream = File.OpenWrite(localPath);
        await stream.CopyToAsync(fileStream);
    }

    private void AddImageToChart(ChartSettings chart,
        SKBitmap chartImage,
        int index,
        int chartImageHeight,
        int chartImageWidth,
        bool largerImages,
        bool validImage,
        string bottomName = null,
        string topName = null,
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

            surface.Canvas.DrawBitmap(chartImage,
                new SKRectI(leftOffset, topOffset, finalWidth + leftOffset, finalHeight + topOffset),
                paint);
            surface.Canvas.Flush();

            using var resizedImage = surface.Snapshot();
            chartImage = SKBitmap.FromImage(resizedImage);
        }

        switch (chart.TitleSetting)
        {
            case TitleSetting.Titles:
                AddTitleToChartImage(chartImage, largerImages, topName, bottomName);
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

        chart.ChartImages.Add(new ChartImage(chartImage, index, validImage, primaryColor, nsfw, censored));
    }

    private void AddTitleToChartImage(SKBitmap chartImage, bool largerImages, string topName = null,
        string bottomName = null)
    {
        var textColor = chartImage.GetTextColor();
        var rectangleColor = textColor == SKColors.Black ? SKColors.White : SKColors.Black;

        using var typeface = SKTypeface.FromFile(this._fontPath);

        var textSize = largerImages ? 17 : 12;

        using var font = new SKFont(typeface)
        {
            Size = textSize
        };

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = textColor
        };

        if ((bottomName != null && font.MeasureText(bottomName, textPaint) > chartImage.Width) ||
            (topName != null && font.MeasureText(topName, textPaint) > chartImage.Width))
        {
            font.Size -= largerImages ? 5 : 2;
        }

        using var rectanglePaint = new SKPaint
        {
            Color = rectangleColor.WithAlpha(140),
            IsAntialias = true
        };

        SKRect topNameBounds;
        SKRect bottomNameBounds;

        using var bitmapCanvas = new SKCanvas(chartImage);

        if (topName != null)
        {
            font.MeasureText(topName, out topNameBounds, textPaint);
        }
        else
        {
            topNameBounds = SKRect.Empty;
        }

        if (bottomName != null)
        {
            font.MeasureText(bottomName, out bottomNameBounds, textPaint);
        }
        else
        {
            bottomNameBounds = SKRect.Empty;
        }

        var rectangleLeft = (chartImage.Width - Math.Max(bottomNameBounds.Width, topNameBounds.Width)) / 2 -
                            (largerImages ? 6 : 3);
        var rectangleRight = (chartImage.Width + Math.Max(bottomNameBounds.Width, topNameBounds.Width)) / 2 +
                             (largerImages ? 6 : 3);

        var rectangleTop = bottomName != null
            ? chartImage.Height - (largerImages ? 44 : 30)
            : chartImage.Height - (largerImages ? 23 : 16);
        var rectangleBottom = chartImage.Height - 1;

        var backgroundRectangle = new SKRect(rectangleLeft, rectangleTop, rectangleRight, rectangleBottom);

        bitmapCanvas.DrawRoundRect(backgroundRectangle, 4, 4, rectanglePaint);

        if (topName != null)
        {
            var yTopName = -topNameBounds.Top + chartImage.Height -
                           (bottomName != null ? (largerImages ? 39 : 26) : (largerImages ? 20 : 13));

            bitmapCanvas.DrawShapedText(topName, (float)chartImage.Width / 2, yTopName, SKTextAlign.Center, font,
                textPaint);
        }

        if (bottomName != null)
        {
            var yBottomName = -bottomNameBounds.Top + chartImage.Height - (largerImages ? 20 : 13);

            bitmapCanvas.DrawShapedText(bottomName, (float)chartImage.Width / 2, yBottomName, SKTextAlign.Center, font,
                textPaint);
        }
    }

    public async Task<ChartSettings> SetSettings(ChartSettings currentChartSettings, UserSettingsModel userSettings,
        bool aoty = false, bool aotd = false)
    {
        var chartSettings = currentChartSettings;
        chartSettings.CustomOptionsEnabled = false;

        var optionsAsString = userSettings.NewSearchValue;
        var splitOptions = optionsAsString?.Split(' ') ?? [];
        var cleanedOptions = optionsAsString;

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
            aoty || aotd ? TimePeriod.AllTime : TimePeriod.Weekly, timeZone: userSettings.TimeZone);

        if (!string.IsNullOrWhiteSpace(timeSettings.NewSearchValue))
        {
            var artist = await this._artistsService.GetArtistFromDatabase(timeSettings.NewSearchValue);
            if (artist != null)
            {
                chartSettings.FilteredArtist = artist;
                timeSettings = SettingService.GetTimePeriod(cleanedOptions, TimePeriod.AllTime,
                    timeZone: userSettings.TimeZone);
            }
        }

        chartSettings.TimeSettings = timeSettings;
        chartSettings.TimespanString = timeSettings.Description;
        chartSettings.TimespanUrlString = timeSettings.UrlParameter;

        return chartSettings;
    }

    public static (ChartSettings newChartSettings, bool Changed) GetDimensions(ChartSettings chartSettings,
        string option)
    {
        var changed = false;
        var matchFound = Regex.IsMatch(option, "^([1-9]|[1-4][0-9]|50)x([1-9]|[1-9]|[1-4][0-9]|50)$",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(300));
        if (matchFound)
        {
            var dimensions = option.ToLower().Split('x').Select(value =>
            {
                var size = int.TryParse(value, out var i) ? i : DefaultChartSize;
                return size;
            }).ToArray();

            chartSettings.Width = dimensions[0];
            chartSettings.Height = dimensions[1];
            changed = true;
        }

        return (chartSettings, changed);
        ;
    }

    public static string AddSettingsToDescription(ChartSettings chartSettings, StringBuilder embedDescription,
        string randomSupporter, string prfx)
    {
        var single = chartSettings.ArtistChart ? "Artist" : "Album";
        var multiple = chartSettings.ArtistChart ? "Artists" : "Albums";

        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            embedDescription.AppendLine($"- Filtering to albums released in {chartSettings.ReleaseYearFilter.Value}");
        }

        if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            embedDescription.AppendLine(
                $"- Filtering to albums released in the {chartSettings.ReleaseDecadeFilter.Value}s");
        }

        if (chartSettings.SkipWithoutImage)
        {
            embedDescription.AppendLine($"- {multiple} without images skipped");
        }

        if (chartSettings.SkipNsfw)
        {
            embedDescription.AppendLine($"- {multiple} with NSFW images skipped");
        }

        if (chartSettings.TitleSetting == TitleSetting.TitlesDisabled)
        {
            embedDescription.AppendLine($"- {single} titles disabled");
        }

        if (chartSettings.FilteredArtist != null)
        {
            embedDescription.AppendLine(
                $"- Filtering to artist **[{chartSettings.FilteredArtist.Name}]({LastfmUrlExtensions.GetArtistUrl(chartSettings.FilteredArtist.Name)})**");
        }

        if (chartSettings.RainbowSortingEnabled)
        {
            embedDescription.AppendLine("- Secret rainbow option enabled! (Not perfect but hey, it somewhat exists)");
        }

        var rnd = new Random();
        if (chartSettings.ImagesNeeded == 1 && rnd.Next(0, 3) == 1 && !chartSettings.ArtistChart)
        {
            embedDescription.AppendLine($"*Linus Tech Tip: Use `{prfx}cover` if you just want to see an album cover.*");
        }

        if (!string.IsNullOrEmpty(randomSupporter))
        {
            embedDescription.AppendLine(
                $"- *Brought to you by .fmbot supporter {StringExtensions.Sanitize(randomSupporter)}.*");
        }

        return embedDescription.ToString();
    }
}
