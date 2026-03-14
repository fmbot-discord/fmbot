using System;
using System.Drawing;
using SkiaSharp;

namespace FMBot.Bot.Extensions;

public static class BitmapExtensions
{
    private static byte MostDifferent(byte original)
    {
        return original < 0x80 ? (byte) 0xff : (byte) 0x00;
    }

    private static Color MostDifferent(Color original)
    {
        var r = MostDifferent(original.R);
        var g = MostDifferent(original.G);
        var b = MostDifferent(original.B);
        return Color.FromArgb(r, g, b);
    }
    
    public static Color GetAverageRgbColor(this SKBitmap skBitmap)
    {
        long totalR = 0;
        long totalG = 0;
        long totalB = 0;
        long totalA = 0;
        var totalPixels = 0;

        for (var x = 0; x < skBitmap.Width; x++)
        {
            for (var y = 0; y < skBitmap.Height; y++)
            {
                var clr = skBitmap.GetPixel(x, y);
                totalR += clr.Red;
                totalG += clr.Green;
                totalB += clr.Blue;
                totalA += clr.Alpha;
                totalPixels++;
            }
        }

        if (totalPixels == 0)
        {
            return Color.Transparent;
        }

        var avgR = (int)(totalR / totalPixels);
        var avgG = (int)(totalG / totalPixels);
        var avgB = (int)(totalB / totalPixels);
        var avgA = (int)(totalA / totalPixels);

        return Color.FromArgb(avgA, avgR, avgG, avgB);
    }

    public static Color GetDominantColor(this SKBitmap skBitmap)
    {
        const int maxSampleSize = 64;

        SKBitmap sampled;
        bool needsDispose;
        if (skBitmap.Width > maxSampleSize || skBitmap.Height > maxSampleSize)
        {
            var scale = Math.Min((float)maxSampleSize / skBitmap.Width, (float)maxSampleSize / skBitmap.Height);
            var newWidth = Math.Max(1, (int)(skBitmap.Width * scale));
            var newHeight = Math.Max(1, (int)(skBitmap.Height * scale));
            sampled = skBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.Low);
            needsDispose = true;
        }
        else
        {
            sampled = skBitmap;
            needsDispose = false;
        }

        try
        {
            double weightedR = 0, weightedG = 0, weightedB = 0;
            double totalWeight = 0;

            for (var x = 0; x < sampled.Width; x++)
            {
                for (var y = 0; y < sampled.Height; y++)
                {
                    var pixel = sampled.GetPixel(x, y);
                    if (pixel.Alpha < 10) continue;

                    var r = pixel.Red / 255.0;
                    var g = pixel.Green / 255.0;
                    var b = pixel.Blue / 255.0;

                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));
                    var chroma = max - min;

                    var weight = 0.1 + chroma * chroma;

                    weightedR += pixel.Red * weight;
                    weightedG += pixel.Green * weight;
                    weightedB += pixel.Blue * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight < 0.001)
            {
                return Color.Transparent;
            }

            return Color.FromArgb(
                255,
                (int)Math.Clamp(weightedR / totalWeight, 0, 255),
                (int)Math.Clamp(weightedG / totalWeight, 0, 255),
                (int)Math.Clamp(weightedB / totalWeight, 0, 255));
        }
        finally
        {
            if (needsDispose)
            {
                sampled.Dispose();
            }
        }
    }

    private static int PerceivedBrightness(Color c)
    {
        return (int) Math.Sqrt(
            c.R * c.R * .299 +
            c.G * c.G * .587 +
            c.B * c.B * .114);
    }

    public static SKColor GetTextColor(this SKBitmap bmp)
    {
        return PerceivedBrightness(MostDifferent(GetAverageRgbColor(bmp))) > 130 ? SKColors.White : SKColors.Black;
    }
}
