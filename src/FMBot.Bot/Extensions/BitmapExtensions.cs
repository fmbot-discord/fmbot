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

    public static Color GetAverageRgbColor(this SKBitmap skbmp)
    {
        var r = 0;
        var g = 0;
        var b = 0;

        var total = 0;

        for (var x = 0; x < skbmp.Width; x++)
        {
            for (var y = 0; y < skbmp.Height; y++)
            {
                var clr = skbmp.GetPixel(x, y);

                r += clr.Red;
                g += clr.Green;
                b += clr.Blue;

                total++;
            }
        }

        total = total == 0 ? 1 : total;

        r /= total;
        g /= total;
        b /= total;

        var color = Color.FromArgb(r, g, b);

        return color;
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
