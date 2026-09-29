using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace FMBot.Core;

public static partial class AccentColors
{
    public const int LastFmRed = 0xBA0000;

    [GeneratedRegex("^#?([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")]
    private static partial Regex HexColorRegex();

    public static string NormalizeHex(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = HexColorRegex().Match(input.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hex = match.Groups[1].Value.ToUpperInvariant();
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }

        return $"#{hex}";
    }

    public static bool TryParseHex(string input, out int rgb)
    {
        rgb = 0;

        var normalized = NormalizeHex(input);
        if (normalized == null)
        {
            return false;
        }

        rgb = int.Parse(normalized.AsSpan(1), NumberStyles.HexNumber);
        return true;
    }

    public static System.Drawing.Color FromBitmap(SKBitmap skBitmap)
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
                return System.Drawing.Color.Transparent;
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
            return System.Drawing.Color.FromArgb(255,
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
}
