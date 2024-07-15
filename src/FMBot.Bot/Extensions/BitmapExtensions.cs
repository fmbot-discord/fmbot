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
