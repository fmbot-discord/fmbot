using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore.Internal;
using SkiaSharp;

namespace FMBot.Bot.Services
{
    public class ChartService : IChartService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        public async Task<SKImage> GenerateChartAsync(ChartSettings chart)
        {
            try
            {
                await chart.Albums.ParallelForEachAsync(async album =>
                {
                    var encodedId = ReplaceInvalidChars(album.Url.LocalPath.Replace("/music/", ""));
                    var localAlbumId = TruncateLongString(encodedId, 60);

                    SKBitmap chartImage;
                    var validImage = true;

                    var fileName = localAlbumId + ".png";
                    var localPath = FMBotUtil.GlobalVars.CacheFolder + fileName;

                    if (File.Exists(localPath))
                    {
                        chartImage = SKBitmap.Decode(localPath);
                        Statistics.LastfmCachedImageCalls.Inc();
                    }
                    else
                    {
                        var albumInfo = await this._lastFMClient.Album.GetInfoAsync(album.ArtistName, album.Name);
                        Statistics.LastfmApiCalls.Inc();

                        var albumImages = albumInfo?.Content?.Images;

                        if (albumImages?.Large != null)
                        {
                            var url = albumImages.Large.AbsoluteUri;

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
                    }


                    chart.ChartImages.Add(new ChartImage(chartImage, chart.Albums.IndexOf(album), validImage));
                });


                SKImage finalImage = null;

                using (var tempSurface = SKSurface.Create(new SKImageInfo(chart.ChartImages.First().Image.Width * chart.Width, chart.ChartImages.First().Image.Height * chart.Height)))
                {
                    var canvas = tempSurface.Canvas;

                    var offset = 0;
                    var offsetTop = 0;
                    var heightRow = 0;

                    for (var i = 0; i < chart.ImagesNeeded; i++)
                    {
                        var image = chart.ChartImages
                            .OrderBy(o => o.Index)
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

            var typeface = SKFontManager.Default.MatchCharacter(album.ArtistName[0]);

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

        private string ReplaceInvalidChars(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        private static string TruncateLongString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            return str.Substring(0, Math.Min(str.Length, maxLength));
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

            // chart size
            if (extraOptions.Contains("2x2"))
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
            else
            {
                chartSettings.ImagesNeeded = 9;
                chartSettings.Height = 3;
                chartSettings.Width = 3;
            }

            // time period
            if (extraOptions.Contains("weekly") || extraOptions.Contains("week") || extraOptions.Contains("w"))
            {
                chartSettings.TimeSpan = LastStatsTimeSpan.Week;
                chartSettings.TimespanString = "Weekly Chart";
                chartSettings.TimespanUrlString = "LAST_7_DAYS";
            }
            else if (extraOptions.Contains("monthly") || extraOptions.Contains("month") || extraOptions.Contains("m"))
            {
                chartSettings.TimeSpan = LastStatsTimeSpan.Month;
                chartSettings.TimespanString = "Monthly Chart";
                chartSettings.TimespanUrlString = "LAST_30_DAYS";
            }
            else if (extraOptions.Contains("yearly") || extraOptions.Contains("year") || extraOptions.Contains("y"))
            {
                chartSettings.TimeSpan = LastStatsTimeSpan.Year;
                chartSettings.TimespanString = "Yearly Chart";
                chartSettings.TimespanUrlString = "LAST_365_DAYS";
            }
            else if (extraOptions.Contains("overall") || extraOptions.Contains("alltime") || extraOptions.Contains("o") || extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                chartSettings.TimeSpan = LastStatsTimeSpan.Overall;
                chartSettings.TimespanString = "Overall Chart";
                chartSettings.TimespanUrlString = "ALL";
            }
            else
            {
                chartSettings.TimeSpan = LastStatsTimeSpan.Week;
                chartSettings.TimespanString = "Chart";
                chartSettings.TimespanUrlString = "LAST_7_DAYS";
            }

            return chartSettings;
        }
    }
}
