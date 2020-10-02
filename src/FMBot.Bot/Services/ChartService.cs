using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Domain;
using IF.Lastfm.Core.Objects;
using SkiaSharp;

namespace FMBot.Bot.Services
{
    public class ChartService : IChartService
    {
        public async Task<SKImage> GenerateChartAsync(ChartSettings chart)
        {
            try
            {
                await chart.Albums.ParallelForEachAsync(async album =>
                {
                    var encodedId = StringExtensions.ReplaceInvalidChars(album.Url.LocalPath.Replace("/music/", ""));
                    var localAlbumId = StringExtensions.TruncateLongString(encodedId, 60);

                    SKBitmap chartImage;
                    var validImage = true;
                    Color? primaryColor = null;

                    var fileName = localAlbumId + ".png";
                    var localPath = FMBotUtil.GlobalVars.CacheFolder + fileName;

                    if (File.Exists(localPath))
                    {
                        chartImage = SKBitmap.Decode(localPath);
                        Statistics.LastfmCachedImageCalls.Inc();
                    }
                    else
                    {
                        if (album.Images.Any() && album.Images.Large != null)
                        {
                            var url = album.Images.Large.AbsoluteUri;

                            SKBitmap bitmap;
                            try
                            {
                                var httpClient = new System.Net.Http.HttpClient();
                                var bytes = await httpClient.GetByteArrayAsync(url);

                                Statistics.LastfmImageCalls.Inc();

                                var stream = new MemoryStream(bytes);

                                bitmap = SKBitmap.Decode(stream);
                            }
                            catch
                            {
                                bitmap = SKBitmap.Decode(FMBotUtil.GlobalVars.ImageFolder + "loading-error.png");
                                validImage = false;
                            }

                            chartImage = bitmap;

                            if (validImage)
                            {
                                using var image = SKImage.FromBitmap(bitmap);
                                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                await using var stream = File.OpenWrite(localPath);
                                data.SaveTo(stream);
                            }
                        }
                        else
                        {
                            chartImage = SKBitmap.Decode(FMBotUtil.GlobalVars.ImageFolder + "unknown.png");
                            validImage = false;
                        }
                    }

                    switch (chart.TitleSetting)
                    {
                        case TitleSetting.Titles:
                            AddTitleToChartImage(chartImage, album);
                            break;
                        case TitleSetting.ClassicTitles:
                            AddClassicTitleToChartImage(chartImage, album);
                            break;
                        case TitleSetting.TitlesDisabled:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (chart.RainbowSortingEnabled)
                    {
                        primaryColor = chartImage.GetAverageRgbColor();
                    }

                    chart.ChartImages.Add(new ChartImage(chartImage, chart.Albums.IndexOf(album), validImage, primaryColor));
                });


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
                            .Where(w => !chart.SkipArtistsWithoutImage || w.ValidImage)
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



        private static void AddTitleToChartImage(SKBitmap chartImage, LastAlbum album)
        {
            var textColor = chartImage.GetTextColor();
            var rectangleColor = textColor == SKColors.Black ? SKColors.White : SKColors.Black;

            var typeface = SKTypeface.FromFile(FMBotUtil.GlobalVars.FontFolder + "arial-unicode-ms.ttf");

            using var textPaint = new SKPaint
            {
                TextSize = 11,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Color = textColor,
                Typeface = typeface
            };

            if (textPaint.MeasureText(album.Name) > chartImage.Width ||
                textPaint.MeasureText(album.ArtistName) > chartImage.Width)
            {
                textPaint.TextSize = 9;
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

            textPaint.MeasureText(album.ArtistName, ref artistBounds);
            textPaint.MeasureText(album.Name, ref albumBounds);

            var rectangleLeft = (chartImage.Width - Math.Max(albumBounds.Width, artistBounds.Width)) / 2 - 3;
            var rectangleRight = (chartImage.Width + Math.Max(albumBounds.Width, artistBounds.Width)) / 2 + 3;
            var rectangleTop = chartImage.Height - 28;
            var rectangleBottom = chartImage.Height - 1;

            var backgroundRectangle = new SKRect(rectangleLeft, rectangleTop, rectangleRight, rectangleBottom);

            bitmapCanvas.DrawRoundRect(backgroundRectangle, 4, 4, rectanglePaint);

            bitmapCanvas.DrawText(album.ArtistName, (float)chartImage.Width / 2, -artistBounds.Top + chartImage.Height - 24,
                textPaint);

            bitmapCanvas.DrawText(album.Name, (float)chartImage.Width / 2, -albumBounds.Top + chartImage.Height - 12,
                textPaint);
        }

        private static void AddClassicTitleToChartImage(SKBitmap chartImage, LastAlbum album)
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
            textPaint.MeasureText(album.Name, ref albumBounds);

            bitmapCanvas.DrawText(album.ArtistName, 4, 12, textPaint);
            bitmapCanvas.DrawText(album.Name, 4, 22, textPaint);
        }

        public ChartSettings SetSettings(ChartSettings currentChartSettings, string[] extraOptions)
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
                chartSettings.SkipArtistsWithoutImage = true;
                chartSettings.CustomOptionsEnabled = true;
            }

            if (extraOptions.Contains("rainbow") ||
                extraOptions.Contains("pride") ||
                extraOptions.Contains("r"))
            {
                chartSettings.RainbowSortingEnabled = true;
                chartSettings.SkipArtistsWithoutImage = true;
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

            var timeSettings = SettingService.GetTimePeriod(extraOptions);

            if (timeSettings.UsePlays)
            {
                // Reset to weekly since using plays for charts is not supported yet
                chartSettings.UsePlays = true;
                timeSettings = SettingService.GetTimePeriod(new []{"weekly"});
            }

            chartSettings.TimeSpan = timeSettings.LastStatsTimeSpan;
            chartSettings.TimespanString = $"{timeSettings.Description} Chart";
            chartSettings.TimespanUrlString = timeSettings.UrlParameter;

            return chartSettings;
        }

    }
}
