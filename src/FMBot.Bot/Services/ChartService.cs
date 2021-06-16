using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using Serilog;
using SkiaSharp;

namespace FMBot.Bot.Services
{
    public class ChartService
    {
        private readonly CensorService _censorService;
        public ChartService(CensorService censorService)
        {
            this._censorService = censorService;

            try
            {
                var filePath = AppDomain.CurrentDomain.BaseDirectory + "arial-unicode-ms.ttf";

                if (!File.Exists(filePath))
                {
                    Log.Information("Downloading chart font...");
                    var wc = new System.Net.WebClient();
                    wc.DownloadFile("https://fmbot.xyz/fonts/arial-unicode-ms.ttf", filePath);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while downloading chart font");
            }
        }

        public async Task<SKImage> GenerateChartAsync(ChartSettings chart, bool nsfwAllowed, bool artistChart)
        {
            try
            {
                var largerImages = true;
                var chartImageHeight = 300;
                var chartImageWidth = 300;

                if (chart.Width > 6)
                {
                    largerImages = false;
                    chartImageHeight = 180;
                    chartImageWidth = 180;
                }

                var httpClient = new System.Net.Http.HttpClient();

                const string loadingErrorImgUrl = "https://fmbot.xyz/img/bot/loading-error.png";
                const string unknownImgUrl = "https://fmbot.xyz/img/bot/unknown.png";
                const string censoredImgUrl = "https://fmbot.xyz/img/bot/censored.png";

                if (!artistChart)
                {
                    await chart.Albums.ParallelForEachAsync(async album =>
                    {
                        var encodedId = StringExtensions.ReplaceInvalidChars(album.AlbumUrl.Replace("https://www.last.fm/music/", ""));
                        var localAlbumId = StringExtensions.TruncateLongString($"album_{encodedId}", 60);

                        SKBitmap chartImage;
                        var validImage = true;

                        var fileName = localAlbumId + ".png";
                        var localPath = Path.Combine(FMBotUtil.GlobalVars.CacheFolder, fileName);


                        if (File.Exists(localPath))
                        {
                            chartImage = SKBitmap.Decode(localPath);
                            Statistics.LastfmCachedImageCalls.Inc();
                        }
                        else
                        {
                            if (album.AlbumCoverUrl != null)
                            {
                                try
                                {
                                    var bytes = await httpClient.GetByteArrayAsync(album.AlbumCoverUrl);

                                    Statistics.LastfmImageCalls.Inc();

                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                }
                                catch (Exception e)
                                {
                                    Log.Error("Error while loading image for generated chart", e);
                                    var bytes = await httpClient.GetByteArrayAsync(loadingErrorImgUrl);
                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                    validImage = false;
                                }

                                if (chartImage == null)
                                {
                                    Log.Error("Error while loading image for generated chart (chartimg null)");
                                    var bytes = await httpClient.GetByteArrayAsync(loadingErrorImgUrl);
                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                    validImage = false;
                                }

                                if (validImage)
                                {
                                    using var image = SKImage.FromBitmap(chartImage);
                                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                    await using var stream = File.OpenWrite(localPath);
                                    data.SaveTo(stream);
                                }
                            }
                            else
                            {
                                var bytes = await httpClient.GetByteArrayAsync(unknownImgUrl);
                                await using var stream = new MemoryStream(bytes);
                                chartImage = SKBitmap.Decode(stream);
                                validImage = false;
                            }
                        }

                        if (!await this._censorService.AlbumIsSafe(album.AlbumName, album.ArtistName))
                        {
                            if (!nsfwAllowed || !await this._censorService.AlbumIsAllowedInNsfw(album.AlbumName, album.ArtistName))
                            {
                                var bytes = await httpClient.GetByteArrayAsync(censoredImgUrl);
                                await using var stream = new MemoryStream(bytes);
                                chartImage = SKBitmap.Decode(stream);
                                validImage = false;
                                if (chart.CensoredAlbums.HasValue)
                                {
                                    chart.CensoredAlbums++;
                                }
                                else
                                {
                                    chart.CensoredAlbums = 1;
                                }
                            }
                        }

                        AddImageToChart(chart, chartImage, chartImageHeight, chartImageWidth, largerImages, validImage, album);
                    });
                }
                else
                {
                    await chart.Artists.ParallelForEachAsync(async artist =>
                    {
                        var encodedId = StringExtensions.ReplaceInvalidChars(artist.ArtistUrl.Replace("https://www.last.fm/music/", ""));
                        var localAlbumId = StringExtensions.TruncateLongString($"artist_{encodedId}", 60);

                        SKBitmap chartImage;
                        var validImage = true;

                        var fileName = localAlbumId + ".png";
                        var localPath = Path.Combine(FMBotUtil.GlobalVars.CacheFolder, fileName);

                        if (File.Exists(localPath))
                        {
                            chartImage = SKBitmap.Decode(localPath);
                        }
                        else
                        {
                            if (artist.ArtistImageUrl != null)
                            {
                                try
                                {
                                    var bytes = await httpClient.GetByteArrayAsync(artist.ArtistImageUrl);
                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                }
                                catch (Exception e)
                                {
                                    Log.Error("Error while loading image for generated artist chart", e);
                                    var bytes = await httpClient.GetByteArrayAsync(loadingErrorImgUrl);
                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                    validImage = false;
                                }

                                if (chartImage == null)
                                {
                                    Log.Error("Error while loading image for generated artist chart (chartimg null)");
                                    var bytes = await httpClient.GetByteArrayAsync(loadingErrorImgUrl);
                                    await using var stream = new MemoryStream(bytes);
                                    chartImage = SKBitmap.Decode(stream);
                                    validImage = false;
                                }

                                if (validImage)
                                {
                                    using var image = SKImage.FromBitmap(chartImage);
                                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                    await using var stream = File.OpenWrite(localPath);
                                    data.SaveTo(stream);
                                }
                            }
                            else
                            {
                                var bytes = await httpClient.GetByteArrayAsync(unknownImgUrl);
                                await using var stream = new MemoryStream(bytes);
                                chartImage = SKBitmap.Decode(stream);
                                validImage = false;
                            }
                        }

                        AddImageToChart(chart, chartImage, chartImageHeight, chartImageWidth, largerImages, validImage, artist: artist);
                    });
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

                        var image = imageList
                            .Where(w => !chart.SkipWithoutImage || w.ValidImage)
                            .ElementAt(i).Image;

                        canvas.DrawBitmap(image, SKRect.Create(offset, offsetTop, image.Width, image.Height));

                        if (i == (chart.Width - 1) || i - (chart.Width) * heightRow == chart.Width - 1)
                        {
                            offsetTop += image.Height;
                            heightRow += 1;
                            offset = 0;
                        }
                        else
                        {
                            offset += image.Width;
                        }
                    }

                    finalImage = tempSurface.Snapshot();
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

        private static void AddImageToChart(ChartSettings chart, SKBitmap chartImage, int chartImageHeight,
            int chartImageWidth, bool largerImages, bool validImage, TopAlbum album = null, TopArtist artist = null)
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

            chart.ChartImages.Add(new ChartImage(chartImage, index, validImage, primaryColor));
        }

        private static void AddTitleToChartImage(SKBitmap chartImage, bool largerImages, TopAlbum album = null, TopArtist artist = null)
        {
            var textColor = chartImage.GetTextColor();
            var rectangleColor = textColor == SKColors.Black ? SKColors.White : SKColors.Black;

            var typeface = SKTypeface.FromFile(AppDomain.CurrentDomain.BaseDirectory + "arial-unicode-ms.ttf");

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
            ICommandContext commandContext)
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

            if (extraOptions.Contains("rainbow") ||
                extraOptions.Contains("pride"))
            {
                chartSettings.RainbowSortingEnabled = true;
                chartSettings.SkipWithoutImage = true;
                chartSettings.CustomOptionsEnabled = true;
            }

            // chart size
            if (extraOptions.Contains("1x1"))
            {
                chartSettings.ImagesNeeded = 1;
                chartSettings.Height = 1;
                chartSettings.Width = 1;
            }
            else if (extraOptions.Contains("2x2"))
            {
                chartSettings.ImagesNeeded = 4;
                chartSettings.Height = 2;
                chartSettings.Width = 2;
            }
            else if (extraOptions.Contains("4x4"))
            {
                chartSettings.ImagesNeeded = 16;
                chartSettings.Height = 4;
                chartSettings.Width = 4;
            }
            else if (extraOptions.Contains("5x5"))
            {
                chartSettings.ImagesNeeded = 25;
                chartSettings.Height = 5;
                chartSettings.Width = 5;
            }
            else if (extraOptions.Contains("6x6"))
            {
                chartSettings.ImagesNeeded = 36;
                chartSettings.Height = 6;
                chartSettings.Width = 6;
            }
            else if (extraOptions.Contains("7x7"))
            {
                chartSettings.ImagesNeeded = 49;
                chartSettings.Height = 7;
                chartSettings.Width = 7;
            }
            else if (extraOptions.Contains("8x8"))
            {
                chartSettings.ImagesNeeded = 64;
                chartSettings.Height = 8;
                chartSettings.Width = 8;
            }
            else if (extraOptions.Contains("9x9"))
            {
                chartSettings.ImagesNeeded = 81;
                chartSettings.Height = 9;
                chartSettings.Width = 9;
            }
            else if (extraOptions.Contains("10x10"))
            {
                chartSettings.ImagesNeeded = 100;
                chartSettings.Height = 10;
                chartSettings.Width = 10;
            }
            else
            {
                chartSettings.ImagesNeeded = 9;
                chartSettings.Height = 3;
                chartSettings.Width = 3;
            }

            var optionsAsString = "";
            if (extraOptions.Any())
            {
                optionsAsString = string.Join(" ", extraOptions);
            }

            var timeSettings = SettingService.GetTimePeriod(optionsAsString);

            if (timeSettings.UsePlays)
            {
                // Reset to weekly since using plays for charts is not supported yet
                chartSettings.UsePlays = true;
                timeSettings = SettingService.GetTimePeriod("weekly");
            }

            chartSettings.TimePeriod = timeSettings.TimePeriod;
            chartSettings.TimespanString = $"{timeSettings.Description}";
            chartSettings.TimespanUrlString = timeSettings.UrlParameter;

            return chartSettings;
        }

        public static string AddSettingsToDescription(ChartSettings chartSettings, string embedDescription, string randomSupporter, string prfx)
        {
            if (chartSettings.CustomOptionsEnabled)
            {
                embedDescription += "Chart options:\n";
            }
            if (chartSettings.SkipWithoutImage)
            {
                embedDescription += "- Albums without images skipped\n";
            }
            if (chartSettings.TitleSetting == TitleSetting.TitlesDisabled)
            {
                embedDescription += "- Album titles disabled\n";
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
            if (chartSettings.ImagesNeeded == 1 && rnd.Next(0, 3) == 1)
            {
                embedDescription += "*Linus Tech Tip: If you want the cover of the album you're currently listening to, use `.fmcover` or `.fmco`.*\n";
            }

            if (chartSettings.UsePlays)
            {
                embedDescription +=
                    "⚠️ Sorry, but using time periods that use your play history isn't supported for this command.\n";
            }

            if (!string.IsNullOrEmpty(randomSupporter))
            {
                embedDescription +=
                    $"*This chart was brought to you by .fmbot supporter {randomSupporter}. Also want to support .fmbot? Check out `{prfx}donate`.*\n";
            }

            return embedDescription;
        }
    }
}
