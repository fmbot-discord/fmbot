using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

namespace FMBot.Bot.Extensions;

public static class BitmapExtensions
{
    extension(SKBitmap skBitmap)
    {
        public Color GetAccentColor()
        {
            const int maxSampleSize = 64;
            const int quantizeShift = 5;

            SKBitmap sampled;
            bool needsDispose;
            if (skBitmap.Width > maxSampleSize || skBitmap.Height > maxSampleSize)
            {
                var scale = Math.Min((float)maxSampleSize / skBitmap.Width, (float)maxSampleSize / skBitmap.Height);
                var newWidth = Math.Max(1, (int)(skBitmap.Width * scale));
                var newHeight = Math.Max(1, (int)(skBitmap.Height * scale));
                sampled = skBitmap.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Nearest));
                needsDispose = true;
            }
            else
            {
                sampled = skBitmap;
                needsDispose = false;
            }

            try
            {
                var bins = new Dictionary<int, (long R, long G, long B, int Count)>();
                var totalPixels = 0;

                for (var x = 0; x < sampled.Width; x++)
                {
                    for (var y = 0; y < sampled.Height; y++)
                    {
                        var pixel = sampled.GetPixel(x, y);
                        if (pixel.Alpha < 10) continue;

                        var key = (pixel.Red >> quantizeShift << 16) |
                                  (pixel.Green >> quantizeShift << 8) |
                                  (pixel.Blue >> quantizeShift);

                        if (bins.TryGetValue(key, out var existing))
                            bins[key] = (existing.R + pixel.Red, existing.G + pixel.Green, existing.B + pixel.Blue, existing.Count + 1);
                        else
                            bins[key] = (pixel.Red, pixel.Green, pixel.Blue, 1);

                        totalPixels++;
                    }
                }

                if (totalPixels == 0)
                {
                    return Color.Transparent;
                }

                var bestKey = -1;
                var bestScore = -1.0;

                foreach (var (key, bin) in bins)
                {
                    var avgR = bin.R / bin.Count;
                    var avgG = bin.G / bin.Count;
                    var avgB = bin.B / bin.Count;

                    var max = Math.Max(avgR, Math.Max(avgG, avgB));
                    var min = Math.Min(avgR, Math.Min(avgG, avgB));
                    var chroma = (max - min) / 255.0;

                    var proportion = (double)bin.Count / totalPixels;
                    var score = proportion * (1.0 + chroma * 3.0);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestKey = key;
                    }
                }

                var best = bins[bestKey];
                return Color.FromArgb(255,
                    (int)(best.R / best.Count),
                    (int)(best.G / best.Count),
                    (int)(best.B / best.Count));
            }
            finally
            {
                if (needsDispose)
                {
                    sampled.Dispose();
                }
            }
        }

        public SKColor GetTextColor()
        {
            var startY = skBitmap.Height * 3 / 4;
            long totalR = 0, totalG = 0, totalB = 0;
            var totalPixels = 0;

            for (var x = 0; x < skBitmap.Width; x++)
            {
                for (var y = startY; y < skBitmap.Height; y++)
                {
                    var clr = skBitmap.GetPixel(x, y);
                    totalR += clr.Red;
                    totalG += clr.Green;
                    totalB += clr.Blue;
                    totalPixels++;
                }
            }

            if (totalPixels == 0)
            {
                return SKColors.White;
            }

            var avg = Color.FromArgb(
                (int)(totalR / totalPixels),
                (int)(totalG / totalPixels),
                (int)(totalB / totalPixels));

            var brightness = (int)Math.Sqrt(
                avg.R * avg.R * .299 +
                avg.G * avg.G * .587 +
                avg.B * avg.B * .114);

            return brightness > 130 ? SKColors.Black : SKColors.White;
        }
    }
}
