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
    }
}
