using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using Serilog;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Color = System.Drawing.Color;

namespace FMBot.Core.Charts;

public class ChartRenderer
{
    public const int DefaultChartSize = 3;

    private readonly MusicCensorService _censorService;

    private readonly string _fontPath;

    private readonly Lazy<SKTypeface> _typeface;
    private readonly string _workSansFontPath;
    private readonly string _loadingErrorImagePath;
    private readonly string _unknownImagePath;
    private readonly string _unknownArtistImagePath;
    private readonly string _censoredImagePath;
    private readonly string _avatarImagePath;

    private readonly HttpClient _client;

    public ChartRenderer(MusicCensorService censorService, HttpClient httpClient)
    {
        this._censorService = censorService;
        this._client = httpClient;

        var cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "bot");
        if (!Directory.Exists(cachePath))
        {
            Directory.CreateDirectory(cachePath);
        }

        this._fontPath = Path.Combine(cachePath, "sourcehansans-medium.otf");
        this._typeface = new Lazy<SKTypeface>(() => SKTypeface.FromFile(this._fontPath));
        this._workSansFontPath = Path.Combine(cachePath, "worksans-regular.otf");
        this._loadingErrorImagePath = Path.Combine(cachePath, "loading-error.png");
        this._unknownImagePath = Path.Combine(cachePath, "unknown.png");
        this._unknownArtistImagePath = Path.Combine(cachePath, "unknown-artist.png");
        this._censoredImagePath = Path.Combine(cachePath, "censored.png");
        this._avatarImagePath = Path.Combine(cachePath, "default-avatar.png");
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
            { "https://fm.bot/img/bot/censored.png", this._censoredImagePath },
            { "https://fm.bot/img/bot/avatar.png", this._avatarImagePath }
        };

        foreach (var file in files)
        {
            _ = Task.Run(() => DownloadFileAsync(file.Key, file.Value));
        }
    }

    private async Task DownloadFileAsync(string url, string filePath)
    {
        try
        {
            using var response = await this._client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while downloading chart files - {url} - {filePath}", url, filePath);
            ;
        }
    }

    public async Task<SKImage> GenerateChartAsync(ChartSettings chart, Func<long, string> playsLabel)
    {
        try
        {
            var cellSize = GetCellSize(chart.Width, chart.Height);
            var chartImageHeight = cellSize;
            var chartImageWidth = cellSize;
            var scale = cellSize / (float)BaseCellSize;

            string PlaysText(long? playcount) =>
                chart.TitleSetting == TitleSetting.TitlesWithPlays && playcount.HasValue
                    ? playsLabel(playcount.Value)
                    : null;

            using var semaphore = new SemaphoreSlim(MaxConcurrentCoverLoads);

            if (!chart.ArtistChart)
            {
                var prepared = await Task.WhenAll(chart.Albums.Select((album, index) => Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var cover = await LoadAlbumCoverAsync(album, cellSize);
                        return PrepareChartImage(chart,
                            cover.Image,
                            index,
                            chartImageHeight,
                            chartImageWidth,
                            scale,
                            cover.ValidImage,
                            topName: chart.FilteredArtist == null ? album.ArtistName : album.AlbumName,
                            bottomName: chart.FilteredArtist == null ? album.AlbumName : null,
                            nsfw: cover.Nsfw,
                            censored: cover.Censored,
                            plays: PlaysText(album.UserPlaycount));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })));

                for (var index = 0; index < chart.Albums.Count; index++)
                {
                    var album = chart.Albums[index];
                    chart.FileDescription.Append($"#{index + 1} {album.AlbumName} by {album.ArtistName}, ");
                    chart.ChartImages.Add(prepared[index]);
                }
            }
            else
            {
                var prepared = await Task.WhenAll(chart.Artists.Select((artist, index) => Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var cover = await LoadArtistImageAsync(artist, cellSize);
                        return PrepareChartImage(chart,
                            cover.Image,
                            index,
                            chartImageHeight,
                            chartImageWidth,
                            scale,
                            cover.ValidImage,
                            topName: artist.ArtistName,
                            nsfw: cover.Nsfw,
                            censored: cover.Censored,
                            plays: PlaysText(artist.UserPlaycount));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })));

                for (var index = 0; index < chart.Artists.Count; index++)
                {
                    var artist = chart.Artists[index];
                    chart.FileDescription.Append($"#{index + 1} {artist.ArtistName}, ");
                    chart.ChartImages.Add(prepared[index]);
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

            var filteredImages = chart.ChartImages
                .Where(w => !chart.SkipNsfw || !w.Nsfw)
                .Where(w => !chart.SkipWithoutImage || w.ValidImage);

            List<ChartImage> sortedImages;
            if (chart.RainbowSortingEnabled)
            {
                sortedImages = RainbowSort(filteredImages);
            }
            else
            {
                sortedImages = filteredImages.OrderBy(o => o.Index).ToList();
            }

            for (var i = 0; i < Math.Min(chart.ImagesNeeded, sortedImages.Count); i++)
            {
                var chartImage = sortedImages.ElementAtOrDefault(i);

                if (chartImage == null)
                {
                    continue;
                }

                canvas.DrawBitmap(chartImage.Image,
                    SKRect.Create(offset, offsetTop, chartImage.Image.Width, chartImage.Image.Height),
                    new SKSamplingOptions());


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

    private const int MaxConcurrentCoverLoads = 4;

    private record LoadedCover(SKBitmap Image, bool ValidImage, bool Nsfw, bool Censored);

    private async Task<LoadedCover> LoadAlbumCoverAsync(TopAlbum album, int cellSize)
    {
        var censorResult = await this._censorService.AlbumResult(album.AlbumName, album.ArtistName);
        var censor = censorResult == CensorResult.NotSafe;
        var nsfw = censorResult == CensorResult.Nsfw;

        if (censor)
        {
            return new LoadedCover(SKBitmap.Decode(this._censoredImagePath), false, nsfw, true);
        }

        var localPath = AlbumUrlToCacheFilePath(album.AlbumName, album.ArtistName);
        var (image, validImage) = await LoadCoverAsync(
            album.AlbumCoverUrl?.Replace("/770x0/", "/"), localPath, cellSize, this._unknownImagePath);

        return new LoadedCover(image, validImage, nsfw, false);
    }

    private async Task<LoadedCover> LoadArtistImageAsync(TopArtist artist, int cellSize)
    {
        var censorResult = await this._censorService.ArtistResult(artist.ArtistName);
        var censor = censorResult == CensorResult.NotSafe;
        var nsfw = censorResult == CensorResult.Nsfw;

        if (censor)
        {
            return new LoadedCover(SKBitmap.Decode(this._censoredImagePath), false, nsfw, true);
        }

        var localPath = ArtistUrlToCacheFilePath(artist.ArtistName);
        var (image, validImage) = await LoadCoverAsync(
            artist.ArtistImageUrl, localPath, cellSize, this._unknownArtistImagePath);

        return new LoadedCover(image, validImage, nsfw, false);
    }

    private async Task<(SKBitmap Image, bool ValidImage)> LoadCoverAsync(string url, string localPath, int cellSize,
        string unknownImagePath)
    {
        var cached = CachedCoverIsUsable(localPath, cellSize) ? SKBitmap.Decode(localPath) : null;
        if (cached != null)
        {
            CoreStatistics.LastfmCachedImageCalls.Inc();
            return (cached, true);
        }

        if (url == null)
        {
            return (SKBitmap.Decode(unknownImagePath), false);
        }

        var (fetched, fromCache) = await FetchCoverAsync(url, localPath);
        if (fetched == null)
        {
            return (SKBitmap.Decode(this._loadingErrorImagePath), false);
        }

        if (!fromCache)
        {
            await SaveCoverToCache(fetched, localPath);
        }

        return (fetched, true);
    }

    private static readonly Regex LastfmImageUrlRegex =
        new(@"^(https://[a-z-]*lastfm[a-z-]*\.freetls\.fastly\.net/i/u/)([0-9a-f]+\.[a-z]+)$", RegexOptions.Compiled);

    private static IEnumerable<string> CoverUrlCandidates(string url)
    {
        yield return url;

        var match = LastfmImageUrlRegex.Match(url);
        if (match.Success)
        {
            yield return $"{match.Groups[1].Value}ar0/{match.Groups[2].Value}";
        }
    }

    private async Task<(SKBitmap Bitmap, bool FromCache)> FetchCoverAsync(string url, string localPath)
    {
        foreach (var candidate in CoverUrlCandidates(url))
        {
            try
            {
                var bytes = await this._client.GetByteArrayAsync(candidate);

                if (candidate.Contains("freetls.fastly.net"))
                {
                    CoreStatistics.LastfmImageCalls.Inc();
                }

                await using var stream = new MemoryStream(bytes);
                var bitmap = SKBitmap.Decode(stream);
                if (bitmap != null)
                {
                    return (bitmap, false);
                }

                Log.Information("Could not decode image for generated chart - {url}", candidate);
            }
            catch (Exception e)
            {
                Log.Information("Error while loading image for generated chart - {url} - {error}", candidate,
                    e.Message);
            }
        }

        if (localPath != null && File.Exists(localPath))
        {
            var cached = SKBitmap.Decode(localPath);
            if (cached != null)
            {
                Log.Information("Using cached image after failed fetch for generated chart - {url}", url);
                return (cached, true);
            }
        }

        Log.Error("Could not load image for generated chart - {url}", url);
        return (null, false);
    }

    public static string AlbumUrlToCacheFilePath(string albumName, string artistName, string extension = ".png")
    {
        var hash = HashString($"{albumName}--{artistName}");
        var fileName = $"album_{hash}{extension}";
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", fileName);
        return localPath;
    }

    public static string ArtistUrlToCacheFilePath(string artistName, string extension = ".png")
    {
        var hash = HashString(artistName);
        var fileName = $"artist_{hash}{extension}";
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", fileName);
        return localPath;
    }

    private static string HashString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public const int ChartQuality = 95;
    public const string ChartFileExtension = ".jpg";

    public static SKData EncodeChart(SKImage chart)
    {
        using var pixmap = chart.PeekPixels();
        return pixmap.Encode(new SKJpegEncoderOptions(ChartQuality, SKJpegEncoderDownsample.Downsample444,
            SKJpegEncoderAlphaOption.Ignore));
    }
    public const int MaxImages = 225;
    public const int BaseCellSize = 300;
    public const int MinCellSize = 200;
    public const int MaxCellSize = 640;
    public const int MaxChartSize = 3200;
    public const int MaxPixelDimension = 16000;
    public const int MaxCachedCoverSize = 640;
    private const int CachedCoverQuality = 100;

    public static int GetCellSize(int width, int height)
    {
        var longestSide = Math.Max(1, Math.Max(width, height));
        var cellSize = Math.Clamp(MaxChartSize / longestSide, MinCellSize, MaxCellSize);
        if (longestSide * cellSize > MaxPixelDimension)
        {
            cellSize = Math.Max(1, MaxPixelDimension / longestSide);
        }

        return cellSize;
    }

    public static async Task SaveCoverToCache(SKBitmap cover, string localPath, bool overwrite = false)
    {
        if (!overwrite && CachedCoverIsAtLeast(localPath, cover.Width, cover.Height))
        {
            return;
        }

        var bitmap = cover;
        var longestSide = Math.Max(cover.Width, cover.Height);
        if (longestSide > MaxCachedCoverSize)
        {
            var ratio = MaxCachedCoverSize / (float)longestSide;
            var info = new SKImageInfo(Math.Max(1, (int)(cover.Width * ratio)), Math.Max(1, (int)(cover.Height * ratio)));
            bitmap = cover.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        var tempPath = $"{localPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Webp, CachedCoverQuality);
            await using (var stream = File.Create(tempPath))
            {
                data.SaveTo(stream);
            }

            File.Move(tempPath, localPath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not write cover to cache - {localPath}", localPath);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
        finally
        {
            if (!ReferenceEquals(bitmap, cover))
            {
                bitmap.Dispose();
            }
        }
    }

    private static bool CachedCoverIsUsable(string localPath, int cellSize)
    {
        if (!File.Exists(localPath))
        {
            return false;
        }

        using var codec = SKCodec.Create(localPath);
        if (codec == null)
        {
            return false;
        }

        return codec.EncodedFormat == SKEncodedImageFormat.Webp ||
               (codec.Info.Width >= cellSize && codec.Info.Height >= cellSize);
    }

    private static bool CachedCoverIsAtLeast(string localPath, int width, int height)
    {
        if (!File.Exists(localPath))
        {
            return false;
        }

        using var codec = SKCodec.Create(localPath);
        if (codec == null)
        {
            return false;
        }

        if (codec.EncodedFormat != SKEncodedImageFormat.Webp)
        {
            return false;
        }

        var cappedWidth = Math.Min(width, MaxCachedCoverSize);
        var cappedHeight = Math.Min(height, MaxCachedCoverSize);
        return codec.Info.Width >= cappedWidth && codec.Info.Height >= cappedHeight;
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

        if (format != SKEncodedImageFormat.Png && File.Exists(cacheFilePath))
        {
            File.Delete(cacheFilePath);
            await Task.Delay(100);
        }

        if (format == SKEncodedImageFormat.Png)
        {
            using var chartImage = SKBitmap.Decode(stream);
            if (chartImage != null)
            {
                await SaveCoverToCache(chartImage, cacheFilePath, overwrite: true);
            }
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

    private ChartImage PrepareChartImage(ChartSettings chart,
        SKBitmap chartImage,
        int index,
        int chartImageHeight,
        int chartImageWidth,
        float scale,
        bool validImage,
        string bottomName = null,
        string topName = null,
        bool nsfw = false,
        bool censored = false,
        string plays = null)
    {
        if (chartImage.Height != chartImageHeight || chartImage.Width != chartImageWidth)
        {
            using var surface = SKSurface.Create(new SKImageInfo(chartImageWidth, chartImageHeight));
            using var paint = new SKPaint { IsAntialias = true };
            using var sourceImage = SKImage.FromBitmap(chartImage);
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

            var ratioBitmap = (float)chartImage.Width / chartImage.Height;
            var ratioMax = (float)chartImageWidth / chartImageHeight;

            SKRect src, dst;
            if (chart.ArtistChart)
            {
                var srcWidth = ratioBitmap > ratioMax ? chartImage.Height * ratioMax : chartImage.Width;
                var srcHeight = ratioBitmap < ratioMax ? chartImage.Width / ratioMax : chartImage.Height;
                var srcLeft = (chartImage.Width - srcWidth) / 2f;
                var srcTop = (chartImage.Height - srcHeight) / 2f;
                src = new SKRect(srcLeft, srcTop, srcLeft + srcWidth, srcTop + srcHeight);
                dst = new SKRect(0, 0, chartImageWidth, chartImageHeight);
            }
            else
            {
                var dstWidth = ratioMax > ratioBitmap ? chartImageHeight * ratioBitmap : chartImageWidth;
                var dstHeight = ratioMax < ratioBitmap ? chartImageWidth / ratioBitmap : chartImageHeight;
                var dstLeft = (chartImageWidth - dstWidth) / 2f;
                var dstTop = (chartImageHeight - dstHeight) / 2f;
                src = new SKRect(0, 0, chartImage.Width, chartImage.Height);
                dst = new SKRect(dstLeft, dstTop, dstLeft + dstWidth, dstTop + dstHeight);
            }

            surface.Canvas.DrawImage(sourceImage, src, dst, sampling, paint);
            surface.Canvas.Flush();

            using var resizedImage = surface.Snapshot();
            var resizedBitmap = SKBitmap.FromImage(resizedImage);
            chartImage.Dispose();
            chartImage = resizedBitmap;
        }

        switch (chart.TitleSetting)
        {
            case TitleSetting.Titles:
                if (scale >= 0.4f)
                {
                    AddTitleToChartImage(chartImage, scale, topName, bottomName);
                }

                break;
            case TitleSetting.TitlesWithPlays:
                if (scale >= 0.4f)
                {
                    AddFadeTitleToChartImage(chartImage, scale, topName, bottomName, plays);
                }

                break;
            case TitleSetting.TitlesDisabled:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        Color? primaryColor = null;
        if (chart.RainbowSortingEnabled)
        {
            primaryColor = AccentColors.FromBitmap(chartImage);
        }

        return new ChartImage(chartImage, index, validImage, primaryColor, nsfw, censored);
    }

    private static List<ChartImage> RainbowSort(IEnumerable<ChartImage> images)
    {
        const float saturationThreshold = 0.15f;

        var chromatic = new List<(ChartImage Image, float Hue, float Lightness)>();
        var achromatic = new List<(ChartImage Image, float Lightness)>();

        foreach (var img in images)
        {
            if (!img.PrimaryColor.HasValue)
            {
                achromatic.Add((img, 0));
                continue;
            }

            var color = img.PrimaryColor.Value;
            var hue = color.GetHue();
            var saturation = color.GetSaturation();
            var lightness = color.GetBrightness();

            if (saturation < saturationThreshold)
            {
                achromatic.Add((img, lightness));
            }
            else
            {
                chromatic.Add((img, hue, lightness));
            }
        }

        var sorted = chromatic
            .OrderBy(o => o.Hue)
            .ThenBy(o => o.Lightness)
            .Select(o => o.Image)
            .ToList();

        sorted.AddRange(achromatic
            .OrderBy(o => o.Lightness)
            .Select(o => o.Image));

        return sorted;
    }

    private void AddTitleToChartImage(SKBitmap chartImage, float scale, string topName = null,
        string bottomName = null)
    {
        var textColor = AccentColors.TextColorFor(chartImage);
        var rectangleColor = textColor == SKColors.Black ? SKColors.White : SKColors.Black;

        var typeface = this._typeface.Value;

        var textSize = 17 * scale;

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
            font.Size -= 5 * scale;
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
                            6 * scale;
        var rectangleRight = (chartImage.Width + Math.Max(bottomNameBounds.Width, topNameBounds.Width)) / 2 +
                             6 * scale;

        var rectangleTop = bottomName != null
            ? chartImage.Height - 44 * scale
            : chartImage.Height - 23 * scale;
        var rectangleBottom = chartImage.Height - 1;

        var backgroundRectangle = new SKRect(rectangleLeft, rectangleTop, rectangleRight, rectangleBottom);

        bitmapCanvas.DrawRoundRect(backgroundRectangle, 4 * scale, 4 * scale, rectanglePaint);

        if (topName != null)
        {
            var yTopName = -topNameBounds.Top + chartImage.Height -
                           (bottomName != null ? 39 : 20) * scale;

            bitmapCanvas.DrawShapedText(topName, (float)chartImage.Width / 2, yTopName, SKTextAlign.Center, font,
                textPaint);
        }

        if (bottomName != null)
        {
            var yBottomName = -bottomNameBounds.Top + chartImage.Height - 20 * scale;

            bitmapCanvas.DrawShapedText(bottomName, (float)chartImage.Width / 2, yBottomName, SKTextAlign.Center, font,
                textPaint);
        }
    }

    private void AddFadeTitleToChartImage(SKBitmap chartImage, float scale, string topName, string bottomName,
        string plays)
    {
        var typeface = this._typeface.Value;

        var fadeHeight = (bottomName != null ? 104 : 80) * scale;
        var fadeTop = chartImage.Height - fadeHeight;

        using var bitmapCanvas = new SKCanvas(chartImage);

        using var fadeShader = SKShader.CreateLinearGradient(
            new SKPoint(0, fadeTop),
            new SKPoint(0, chartImage.Height),
            [SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(110), SKColors.Black.WithAlpha(170), SKColors.Black.WithAlpha(215)],
            [0f, 0.35f, 0.65f, 1f],
            SKShaderTileMode.Clamp);
        using var fadePaint = new SKPaint { Shader = fadeShader };
        bitmapCanvas.DrawRect(0, fadeTop, chartImage.Width, fadeHeight, fadePaint);

        using var shadow = SKImageFilter.CreateDropShadow(0, 1 * scale, 2 * scale, 2 * scale,
            SKColors.Black.WithAlpha(150));

        using var topFont = new SKFont(typeface) { Size = 17 * scale };
        using var bottomFont = new SKFont(typeface) { Size = 15 * scale };
        using var playsFont = new SKFont(typeface) { Size = 13 * scale };

        using var topPaint = new SKPaint { IsAntialias = true, Color = SKColors.White, ImageFilter = shadow };
        using var bottomPaint = new SKPaint
            { IsAntialias = true, Color = SKColors.White.WithAlpha(225), ImageFilter = shadow };
        using var playsPaint = new SKPaint
            { IsAntialias = true, Color = SKColors.White.WithAlpha(185), ImageFilter = shadow };

        var left = 10 * scale;
        var maxWidth = chartImage.Width - left * 2;
        var y = chartImage.Height - 9 * scale;

        if (plays != null)
        {
            playsFont.MeasureText("0", out var digitBounds, playsPaint);
            bitmapCanvas.DrawShapedText(FitText(plays, playsFont, playsPaint, maxWidth), left, y, SKTextAlign.Left,
                playsFont, playsPaint);
            y -= digitBounds.Height + 8.5f * scale;
        }

        if (bottomName != null)
        {
            bottomFont.MeasureText("H", out var capBounds, bottomPaint);
            bitmapCanvas.DrawShapedText(FitText(bottomName, bottomFont, bottomPaint, maxWidth), left, y,
                SKTextAlign.Left, bottomFont, bottomPaint);
            y -= capBounds.Height + 7.5f * scale;
        }

        if (topName != null)
        {
            bitmapCanvas.DrawShapedText(FitText(topName, topFont, topPaint, maxWidth), left, y, SKTextAlign.Left,
                topFont, topPaint);
        }
    }

    private static string FitText(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (font.MeasureText(text, paint) <= maxWidth)
        {
            return text;
        }

        var length = font.BreakText(text, maxWidth - font.MeasureText("…", paint), paint);
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return $"{text[..length].TrimEnd()}…";
    }

    public static (ChartSettings newChartSettings, bool Changed) GetDimensions(ChartSettings chartSettings,
        string option)
    {
        var changed = false;
        var matchFound = Regex.IsMatch(option, "^([1-9][0-9]{0,2})x([1-9][0-9]{0,2})$",
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
        else
        {
            if (chartSettings.Width == 0)
            {
                chartSettings.Width = 3;
            }
            if (chartSettings.Height == 0)
            {
                chartSettings.Height = 3;
            }
        }

        return (chartSettings, changed);
    }
}
