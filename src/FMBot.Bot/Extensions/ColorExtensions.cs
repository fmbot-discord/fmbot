using System;
using NetCord;
using SkiaSharp;

namespace FMBot.Bot.Extensions;

public static partial class ColorExtensions
{
    public static string NormalizeHexColor(string input)
    {
        return Core.AccentColors.NormalizeHex(input);
    }

    public static bool TryParseHexColor(string input, out Color color)
    {
        color = default;

        if (!Core.AccentColors.TryParseHex(input, out var rgb))
        {
            return false;
        }

        color = new Color(rgb);
        return true;
    }

    extension(SKBitmap skBitmap)
    {
        public System.Drawing.Color GetAccentColor()
        {
            return Core.AccentColors.FromBitmap(skBitmap);
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

            var avg = System.Drawing.Color.FromArgb(
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
