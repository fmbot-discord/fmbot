using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FMBot.Core.Charts;

public static class FontChain
{
    public record Entry(SKTypeface Typeface, SKShaper Shaper, float Scale, bool Rtl);

    private static readonly string CachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "bot");

    private static readonly (string FileName, float Scale, bool Rtl)[] DownloadedFonts =
    [
        ("sourcesans3-medium.otf", 1.11f, false),
        ("sourcehansans-medium.otf", 1f, false),
        ("notosansarabic-medium.ttf", 1f, true),
        ("notosanshebrew-medium.ttf", 1f, true),
        ("notosansthai-medium.ttf", 1f, false)
    ];

    private static readonly string[] EmojiFontPaths =
    [
        "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf",
        @"C:\Windows\Fonts\seguiemj.ttf",
        "/System/Library/Fonts/Apple Color Emoji.ttc"
    ];

    private static readonly Lazy<Entry[]> LazyEntries = new(Load);

    public static IEnumerable<(string Url, string Path)> Downloads =>
        DownloadedFonts.Select(s => ($"https://fm.bot/fonts/{s.FileName}", Path.Combine(CachePath, s.FileName)));

    public static Entry[] Entries => LazyEntries.Value;

    private static Entry[] Load()
    {
        return
        [
            .. DownloadedFonts
                .Select(s => (Path: Path.Combine(CachePath, s.FileName), s.Scale, s.Rtl))
                .Where(w => File.Exists(w.Path))
                .Concat(EmojiFontPaths.Where(File.Exists).Take(1).Select(s => (Path: s, Scale: 1f, Rtl: false)))
                .Select(s => (Typeface: SKTypeface.FromFile(s.Path), s.Scale, s.Rtl))
                .Where(w => w.Typeface != null)
                .Select(s => new Entry(s.Typeface, new SKShaper(s.Typeface), s.Scale, s.Rtl))
        ];
    }
}
