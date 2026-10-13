using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SkiaSharp;

namespace FMBot.Core.Charts;

public sealed class FallbackFont : IDisposable
{
    private readonly FontChain.Entry[] _entries;
    private readonly SKFont[] _fonts;
    private float _size;

    private record ShapedRun(SKFont Font, ushort[] Glyphs, SKPoint[] Points, float Width);

    public FallbackFont(float size, SKFontEdging edging = SKFontEdging.Antialias, bool subpixel = false)
    {
        this._entries = FontChain.Entries;
        this._fonts = this._entries
            .Select(s => new SKFont(s.Typeface, size * s.Scale) { Edging = edging, Subpixel = subpixel })
            .ToArray();
        this._size = size;
    }

    public float Size
    {
        get => this._size;
        set
        {
            this._size = value;
            for (var i = 0; i < this._fonts.Length; i++)
            {
                this._fonts[i].Size = value * this._entries[i].Scale;
            }
        }
    }

    public float MeasureText(string text)
    {
        return Shape(text).Sum(s => s.Width);
    }

    public float MeasureText(string text, out SKRect bounds)
    {
        var runs = Shape(text);
        bounds = SKRect.Empty;

        foreach (var run in runs)
        {
            var glyphBounds = new SKRect[run.Glyphs.Length];
            run.Font.GetGlyphWidths(run.Glyphs, new float[run.Glyphs.Length], glyphBounds, null);

            for (var i = 0; i < glyphBounds.Length; i++)
            {
                if (glyphBounds[i].IsEmpty)
                {
                    continue;
                }

                var glyph = glyphBounds[i];
                glyph.Offset(run.Points[i]);
                bounds = bounds.IsEmpty ? glyph : SKRect.Union(bounds, glyph);
            }
        }

        return runs.Sum(s => s.Width);
    }

    public bool ContainsGlyphs(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (!this._fonts.Any(a => Covers(a, element)))
            {
                return false;
            }
        }

        return true;
    }

    public string FitText(string text, float maxWidth)
    {
        if (MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var elementStarts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = elementStarts.Length - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (MeasureText(Truncate(text, elementStarts[mid])) <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Truncate(text, elementStarts[low]);
    }

    public void DrawText(SKCanvas canvas, string text, float x, float y, SKTextAlign textAlign, SKPaint paint)
    {
        var runs = Shape(text);
        if (runs.Count == 0)
        {
            return;
        }

        var width = runs.Sum(s => s.Width);
        var alignOffset = textAlign switch
        {
            SKTextAlign.Center => width / 2,
            SKTextAlign.Right => width,
            _ => 0f
        };

        using var builder = new SKTextBlobBuilder();
        foreach (var run in runs)
        {
            builder.AddPositionedRun(run.Glyphs, run.Font, run.Points);
        }

        using var textBlob = builder.Build();
        if (textBlob != null)
        {
            canvas.DrawText(textBlob, x - alignOffset, y, paint);
        }
    }

    private static string Truncate(string text, int length)
    {
        return $"{text[..length].TrimEnd()}…";
    }

    private List<ShapedRun> Shape(string text)
    {
        var shaped = new List<ShapedRun>();
        if (string.IsNullOrEmpty(text) || this._fonts.Length == 0)
        {
            return shaped;
        }

        var x = 0f;
        foreach (var (fontIndex, runText) in SplitRuns(text))
        {
            var font = this._fonts[fontIndex];
            var result = this._entries[fontIndex].Shaper.Shape(runText, x, 0, font);
            if (result.Codepoints.Length > 0)
            {
                shaped.Add(new ShapedRun(font, result.Codepoints.Select(s => (ushort)s).ToArray(), result.Points,
                    result.Width));
            }

            x += result.Width;
        }

        return shaped;
    }

    private List<(int FontIndex, string Text)> SplitRuns(string text)
    {
        var runs = new List<(int FontIndex, string Text)>();
        var builder = new StringBuilder();
        var current = -1;
        var trailing = 0;

        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var letter = char.IsLetter(element, 0);
            var fontIndex = current >= 0 && !letter && Covers(this._fonts[current], element)
                ? current
                : FontIndexFor(element);

            if (fontIndex != current && builder.Length > 0)
            {
                AddRun(runs, current, builder.ToString(), trailing);
                builder.Clear();
                trailing = 0;
            }

            current = fontIndex;
            builder.Append(element);
            trailing = letter ? 0 : trailing + element.Length;
        }

        if (builder.Length > 0)
        {
            AddRun(runs, current, builder.ToString(), trailing);
        }

        return runs;
    }

    private void AddRun(List<(int FontIndex, string Text)> runs, int fontIndex, string text, int trailing)
    {
        if (this._entries[fontIndex].Rtl && trailing > 0 && trailing < text.Length)
        {
            runs.Add((fontIndex, text[..^trailing]));
            runs.Add((fontIndex, text[^trailing..]));
            return;
        }

        runs.Add((fontIndex, text));
    }

    private int FontIndexFor(string element)
    {
        for (var i = 0; i < this._fonts.Length; i++)
        {
            if (Covers(this._fonts[i], element))
            {
                return i;
            }
        }

        return 0;
    }

    private static bool Covers(SKFont font, string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (!font.ContainsGlyph(rune.Value))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        foreach (var font in this._fonts)
        {
            font.Dispose();
        }
    }
}
