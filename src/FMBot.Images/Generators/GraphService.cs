using System.Globalization;
using FMBot.Domain.Models;
using FMBot.Images.Models;
using Serilog;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FMBot.Images.Generators;

public class GraphService
{
    private const float RenderScale = 2f;
    private const float BaseWidth = 540f;
    private const float FontSize = 14f;
    private const float PaddingTop = 10f;
    private const float EdgeMargin = 6f;
    private const float PaddingRight = EdgeMargin + DotRadius;
    private const float AxisHeight = FontSize * 1.6f;
    private const float LabelSpacing = 7f;
    private const float TickLength = 5f;
    private const float LabelBaseline = FontSize * 1.33f;
    private const float LabelCenter = FontSize * 0.33f;
    private const float LineWidth = 2.5f;
    private const float DotRadius = 4.5f;
    private const float BarGap = 2f;
    private const float LegendHeight = FontSize * 1.6f;
    private const double MinimumPlays = 5;

    private static readonly SKColor AxisColor = new(0x8B, 0x9B, 0xA0);
    private static readonly SKColor LabelColor = new(0x93, 0xA1, 0xA6);

    private SKTypeface _typeface;

    private SKTypeface GetTypeface()
    {
        if (this._typeface != null)
        {
            return this._typeface;
        }

        var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "bot",
            "sourcehansans-medium.otf");

        if (!File.Exists(fontPath))
        {
            return SKTypeface.Default;
        }

        var typeface = SKTypeface.FromFile(fontPath);
        if (typeface == null)
        {
            return SKTypeface.Default;
        }

        this._typeface = typeface;
        return typeface;
    }

    public PlayHistoryGraph RenderPlayHistory(IReadOnlyList<GraphPoint> dailyPlays, CultureInfo culture,
        SKColor lineColor, Func<double, string> valueLabel, GraphInterval? fixedInterval = null,
        DateTime? windowFrom = null, DateTime? windowUntil = null, int width = 660, int height = 165,
        GraphType style = GraphType.Line)
    {
        if (dailyPlays == null || dailyPlays.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var until = windowUntil.HasValue && windowUntil.Value < now ? windowUntil.Value : now;
        var earliest = windowFrom ?? dailyPlays[0].Date;

        var totalPlays = 0d;
        foreach (var day in dailyPlays)
        {
            totalPlays += day.Value;
        }

        var sampleCount = totalPlays >= int.MaxValue ? int.MaxValue : (int)totalPlays;
        var interval = fixedInterval ?? GraphSeries.PickInterval(earliest, until, sampleCount, style);

        if (style == GraphType.Bar && fixedInterval.HasValue)
        {
            var barInterval = GraphSeries.PickInterval(earliest, until, sampleCount, style);
            if (barInterval > interval)
            {
                interval = barInterval;
            }
        }

        var bar = style == GraphType.Bar;

        if (!windowFrom.HasValue && !bar)
        {
            var earliestStart = GraphSeries.EarliestStart(until, interval);
            earliest = earliest < earliestStart ? earliest : earliestStart;
        }

        var from = bar ? earliest : GraphSeries.LimitToMaxPoints(earliest, until, interval);

        if (interval != GraphInterval.Day && !bar)
        {
            (from, until) = GraphSeries.TrimToWholeIntervals(from, until, interval);
        }

        var points = GraphSeries.FromDailyCounts(dailyPlays, interval, from, until,
            bar && windowFrom.HasValue ? from : null);

        if (!windowFrom.HasValue && !bar)
        {
            TrimLeadingEmpty(points, GraphSeries.EarliestStart(until, interval));
        }

        if ((!bar && points.Count < 3) || points.Count(w => w.Value > 0) < 2 || points.Sum(s => s.Value) < MinimumPlays)
        {
            return null;
        }

        var image = RenderLineGraph(new LineGraph
        {
            Points = points,
            Width = width,
            Height = height,
            LineColor = lineColor,
            ValueLabel = valueLabel,
            Culture = culture,
            Interval = interval,
            Style = style
        });

        return image == null
            ? null
            : new PlayHistoryGraph
            {
                Image = image,
                Interval = interval
            };
    }

    public PlayHistoryGraph RenderStackedPlayHistory(IReadOnlyList<GraphPoint> dailyBase,
        IReadOnlyList<GraphPoint> dailyTop, GraphLegendItem baseLegend, GraphLegendItem topLegend,
        CultureInfo culture, Func<double, string> valueLabel, string legendNote = null, int width = 660,
        int height = 190)
    {
        var dailyTotals = dailyBase
            .Concat(dailyTop)
            .GroupBy(g => g.Date)
            .OrderBy(o => o.Key)
            .Select(s => new GraphPoint
            {
                Date = s.Key,
                Value = s.Sum(v => v.Value)
            })
            .ToList();

        if (dailyTotals.Count == 0)
        {
            return null;
        }

        var from = dailyTotals[0].Date;
        var until = DateTime.UtcNow;

        var totalPlays = dailyTotals.Sum(s => s.Value);
        var sampleCount = totalPlays >= int.MaxValue ? int.MaxValue : (int)totalPlays;
        var interval = GraphSeries.PickInterval(from, until, sampleCount, GraphType.Bar);

        var points = GraphSeries.FromDailyCounts(dailyTotals, interval, from, until);
        var basePoints = GraphSeries.FromDailyCounts(dailyBase, interval, from, until);

        if (points.Count(w => w.Value > 0) < 2 || totalPlays < MinimumPlays)
        {
            return null;
        }

        var image = RenderLineGraph(new LineGraph
        {
            Points = points,
            BasePoints = basePoints,
            BaseColor = baseLegend.Color,
            LineColor = topLegend.Color,
            Legend = [baseLegend, topLegend],
            LegendNote = legendNote,
            Width = width,
            Height = height,
            ValueLabel = valueLabel,
            Culture = culture,
            Interval = interval,
            Style = GraphType.Bar
        });

        return image == null
            ? null
            : new PlayHistoryGraph
            {
                Image = image,
                Interval = interval
            };
    }

    private static void TrimLeadingEmpty(List<GraphPoint> points, DateTime keepFrom)
    {
        var firstWithPlays = points.FindIndex(f => f.Value > 0);
        if (firstWithPlays <= 0)
        {
            return;
        }

        var guaranteed = points.FindIndex(f => f.Date >= keepFrom);
        var trim = guaranteed < 0 ? firstWithPlays : Math.Min(firstWithPlays, guaranteed);

        if (trim > 0)
        {
            points.RemoveRange(0, trim);
        }
    }

    private MemoryStream RenderLineGraph(LineGraph graph)
    {
        try
        {
            return Render(graph);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to render line graph");
            return null;
        }
    }

    private MemoryStream Render(LineGraph graph)
    {
        if (graph?.Points == null || graph.Points.Count < 2)
        {
            return null;
        }

        var dataMin = double.MaxValue;
        var dataMax = double.MinValue;
        foreach (var point in graph.Points)
        {
            dataMin = Math.Min(dataMin, point.Value);
            dataMax = Math.Max(dataMax, point.Value);
        }

        var scale = graph.Width / BaseWidth;
        var barStyle = graph.Style == GraphType.Bar;

        using var font = new SKFont(GetTypeface())
        {
            Size = FontSize * scale,
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };
        using var valueFont = new SKFont(GetTypeface())
        {
            Size = FontSize * 0.8f * scale,
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = LabelColor
        };

        var plotRight = graph.Width - PaddingRight * scale;
        var plotTop = PaddingTop * scale;
        var legendHeight = graph.Legend.Count > 0 ? LegendHeight * scale : 0;
        var plotBottom = graph.Height - AxisHeight * scale - legendHeight;
        var plotHeight = plotBottom - plotTop;
        var maxYTicks = Math.Clamp((int)(plotHeight / (FontSize * 1.3f * scale)) + 1, 2, graph.MaxYTicks);
        var valueGap = 2f * scale;

        var barValues = barStyle
            ? BarValueLabels(graph, valueFont, labelPaint, (plotRight - EdgeMargin * scale) / graph.Points.Count)
            : null;

        var axis = barValues == null
            ? LayoutYAxis(graph, font, labelPaint, dataMin, dataMax, maxYTicks, scale)
            : ValueOnlyAxis(graph, valueFont, labelPaint, dataMin, dataMax, plotTop, plotHeight, valueGap, scale);

        var plotLeft = axis.PlotLeft;
        var plotWidth = plotRight - plotLeft;

        if (plotWidth < 60 || plotHeight < 30)
        {
            return null;
        }

        var range = axis.Max - axis.Min;
        var xPositions = new float[graph.Points.Count];
        var yPositions = new float[graph.Points.Count];
        for (var i = 0; i < graph.Points.Count; i++)
        {
            xPositions[i] = barStyle
                ? plotLeft + (i + 0.5f) / graph.Points.Count * plotWidth
                : plotLeft + (float)i / (graph.Points.Count - 1) * plotWidth;
            yPositions[i] = plotTop + (float)(1 - (graph.Points[i].Value - axis.Min) / range) * plotHeight;
        }

        var ticks = PlanDateTicks(graph, xPositions, font, labelPaint, scale);

        var imageInfo = new SKImageInfo((int)(graph.Width * RenderScale), (int)(graph.Height * RenderScale),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(RenderScale);

        using var gridPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            Color = AxisColor.WithAlpha(56)
        };
        using var axisPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            Color = AxisColor.WithAlpha(115)
        };

        for (var i = 0; i < axis.Labels.Length; i++)
        {
            var y = plotTop + (float)(1 - i * axis.Step / range) * plotHeight;

            canvas.DrawLine(plotLeft, y, plotRight, y, gridPaint);
            canvas.DrawShapedText(axis.Labels[i], plotLeft - LabelSpacing * scale, y + LabelCenter * scale,
                SKTextAlign.Right, font, labelPaint);
        }

        if (axis.Labels.Length > 0)
        {
            canvas.DrawLine(plotLeft, plotTop, plotLeft, plotBottom, axisPaint);
        }

        canvas.DrawLine(plotLeft, plotBottom, plotRight, plotBottom, axisPaint);

        if (barStyle)
        {
            if (graph.BasePoints != null)
            {
                var basePositions = new float[graph.Points.Count];
                for (var i = 0; i < graph.Points.Count; i++)
                {
                    basePositions[i] = plotTop +
                                       (float)(1 - (graph.BasePoints[i].Value - axis.Min) / range) * plotHeight;
                }

                yPositions = DrawStackedBars(canvas, graph, xPositions, yPositions, basePositions, plotBottom,
                    plotWidth, scale);
            }
            else
            {
                DrawBars(canvas, graph, xPositions, yPositions, plotBottom, plotWidth, scale);
            }

            DrawBarValues(canvas, barValues, xPositions, yPositions, plotBottom, valueGap, scale, valueFont);
            DrawDateLabels(canvas, graph, ticks, xPositions, plotBottom, scale, font, labelPaint);
            DrawLegend(canvas, graph, plotLeft, scale, valueFont, labelPaint);

            return EncodeSurface(surface);
        }

        using var pathBuilder = new SKPathBuilder();
        pathBuilder.MoveTo(xPositions[0], yPositions[0]);
        for (var i = 1; i < xPositions.Length; i++)
        {
            pathBuilder.LineTo(xPositions[i], yPositions[i]);
        }

        using var linePath = pathBuilder.Snapshot();

        if (graph.ShowArea)
        {
            pathBuilder.LineTo(xPositions[^1], plotBottom);
            pathBuilder.LineTo(xPositions[0], plotBottom);
            pathBuilder.Close();
            using var areaPath = pathBuilder.Detach();

            using var areaShader = SKShader.CreateLinearGradient(
                new SKPoint(plotLeft, plotTop),
                new SKPoint(plotLeft, plotBottom),
                [graph.LineColor.WithAlpha(89), graph.LineColor.WithAlpha(5)],
                SKShaderTileMode.Clamp);
            using var areaPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = areaShader
            };

            canvas.DrawPath(areaPath, areaPaint);
        }

        using var linePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LineWidth * scale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = graph.LineColor
        };
        canvas.DrawPath(linePath, linePaint);

        if (graph.ShowEndDot)
        {
            using var dotPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = graph.LineColor
            };
            canvas.DrawCircle(xPositions[^1], yPositions[^1], DotRadius * scale, dotPaint);
        }

        DrawDateLabels(canvas, graph, ticks, xPositions, plotBottom, scale, font, labelPaint);

        return EncodeSurface(surface);
    }

    private static MemoryStream EncodeSurface(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;

        return stream;
    }

    private static void DrawBars(SKCanvas canvas, LineGraph graph, float[] xPositions, float[] yPositions, float plotBottom, float plotWidth, float scale)
    {
        var slot = plotWidth / graph.Points.Count;
        var barWidth = Math.Max(Math.Min(slot * 0.8f, slot - scale), scale);
        var capHeight = LineWidth * scale;
        var cornerRadius = Math.Min(2.5f * scale, barWidth / 2);

        for (var i = 0; i < graph.Points.Count; i++)
        {
            if (graph.Points[i].Value <= 0)
            {
                continue;
            }

            var top = Math.Min(yPositions[i], plotBottom - capHeight);
            DrawBarSegment(canvas,
                new SKRect(xPositions[i] - barWidth / 2, top, xPositions[i] + barWidth / 2, plotBottom),
                graph.LineColor, cornerRadius, capHeight);
        }
    }

    private static float[] DrawStackedBars(SKCanvas canvas, LineGraph graph, float[] xPositions, float[] yPositions,
        float[] basePositions, float plotBottom, float plotWidth, float scale)
    {
        var slot = plotWidth / graph.Points.Count;
        var barWidth = Math.Max(Math.Min(slot * 0.8f, slot - scale), scale);
        var capHeight = LineWidth * scale;
        var gap = BarGap * scale;
        var cornerRadius = Math.Min(2.5f * scale, barWidth / 2);

        var barTops = (float[])yPositions.Clone();

        for (var i = 0; i < graph.Points.Count; i++)
        {
            var baseValue = graph.BasePoints[i].Value;
            var topValue = graph.Points[i].Value - baseValue;
            var left = xPositions[i] - barWidth / 2;
            var right = xPositions[i] + barWidth / 2;
            var segmentBottom = plotBottom;

            if (baseValue > 0)
            {
                var baseTop = topValue > 0
                    ? Math.Max(basePositions[i], yPositions[i] + capHeight + gap)
                    : basePositions[i];
                baseTop = Math.Min(baseTop, plotBottom - capHeight);
                DrawBarSegment(canvas, new SKRect(left, baseTop, right, plotBottom), graph.BaseColor,
                    topValue > 0 ? 0 : cornerRadius, capHeight);

                barTops[i] = baseTop;
                segmentBottom = baseTop - gap;
            }

            if (topValue > 0)
            {
                var top = Math.Min(yPositions[i], segmentBottom - capHeight);
                DrawBarSegment(canvas, new SKRect(left, top, right, segmentBottom), graph.LineColor, cornerRadius,
                    capHeight);

                barTops[i] = top;
            }
        }

        return barTops;
    }

    private static void DrawBarSegment(SKCanvas canvas, SKRect rect, SKColor color, float cornerRadius,
        float capHeight)
    {
        using var roundRect = new SKRoundRect();
        roundRect.SetRectRadii(rect,
        [
            new SKPoint(cornerRadius, cornerRadius), new SKPoint(cornerRadius, cornerRadius),
            new SKPoint(0, 0), new SKPoint(0, 0)
        ]);

        using var fillShader = SKShader.CreateLinearGradient(
            new SKPoint(0, rect.Top),
            new SKPoint(0, rect.Bottom),
            [color.WithAlpha(130), color.WithAlpha(35)],
            SKShaderTileMode.Clamp);
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = fillShader
        };
        using var capPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = color
        };

        canvas.DrawRoundRect(roundRect, fillPaint);

        canvas.Save();
        canvas.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + capHeight));
        canvas.DrawRoundRect(roundRect, capPaint);
        canvas.Restore();
    }

    private static void DrawLegend(SKCanvas canvas, LineGraph graph, float left, float scale, SKFont font,
        SKPaint labelPaint)
    {
        if (graph.Legend.Count == 0)
        {
            return;
        }

        var swatchSize = font.Size * 0.8f;
        var centerY = graph.Height - LegendHeight * scale / 2;
        var x = left;

        using var swatchPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        foreach (var item in graph.Legend)
        {
            swatchPaint.Color = item.Color;
            canvas.DrawRoundRect(
                new SKRect(x, centerY - swatchSize / 2, x + swatchSize, centerY + swatchSize / 2),
                2 * scale, 2 * scale, swatchPaint);

            x += swatchSize + font.Size * 0.45f;
            canvas.DrawShapedText(item.Label, x, centerY + font.Size * 0.35f, SKTextAlign.Left, font, labelPaint);

            x += font.MeasureText(item.Label, labelPaint) + font.Size * 1.4f;
        }

        if (string.IsNullOrWhiteSpace(graph.LegendNote))
        {
            return;
        }

        var noteRight = graph.Width - PaddingRight * scale;
        if (noteRight - font.MeasureText(graph.LegendNote, labelPaint) < x)
        {
            return;
        }

        canvas.DrawShapedText(graph.LegendNote, noteRight, centerY + font.Size * 0.35f, SKTextAlign.Right, font,
            labelPaint);
    }

    private static void DrawBarValues(SKCanvas canvas, string[] barValues, float[] xPositions, float[] yPositions,
        float plotBottom, float valueGap, float scale, SKFont valueFont)
    {
        if (barValues == null)
        {
            return;
        }

        using var valuePaint = new SKPaint
        {
            IsAntialias = true,
            Color = LabelColor.WithAlpha(190)
        };

        for (var i = 0; i < barValues.Length; i++)
        {
            if (barValues[i] == null)
            {
                continue;
            }

            var top = Math.Min(yPositions[i], plotBottom - LineWidth * scale);
            canvas.DrawShapedText(barValues[i], xPositions[i], top - valueGap, SKTextAlign.Center, valueFont,
                valuePaint);
        }
    }

    private static string[] BarValueLabels(LineGraph graph, SKFont valueFont, SKPaint paint, float slot)
    {
        if (slot < valueFont.Size * 1.4f)
        {
            return null;
        }

        return FittingBarValues(graph, valueFont, paint, slot, value => FormatValue(graph, value)) ??
               FittingBarValues(graph, valueFont, paint, slot, value => CompactValue(graph, value));
    }

    private static string[] FittingBarValues(LineGraph graph, SKFont valueFont, SKPaint paint, float slot,
        Func<double, string> format)
    {
        var labels = new string[graph.Points.Count];
        for (var i = 0; i < graph.Points.Count; i++)
        {
            if (graph.Points[i].Value <= 0)
            {
                continue;
            }

            labels[i] = format(graph.Points[i].Value);
            if (valueFont.MeasureText(labels[i], paint) > slot - valueFont.Size * 0.5f)
            {
                return null;
            }
        }

        return labels;
    }

    private static string CompactValue(LineGraph graph, double value)
    {
        var (divisor, suffix) = value >= 1000000 ? (1000000d, "M") : value >= 1000 ? (1000d, "k") : (1d, "");
        var scaled = value / divisor;

        return FormatValue(graph, divisor == 1 ? scaled : Math.Round(scaled, scaled < 10 ? 1 : 0)) + suffix;
    }

    private List<GraphTick> PlanDateTicks(LineGraph graph, float[] xPositions, SKFont font, SKPaint paint, float scale)
    {
        var currentYear = DateTime.UtcNow.Year;

        bool Fits(List<GraphTick> ticks)
        {
            var previousRight = float.MinValue;
            foreach (var tick in ticks)
            {
                var (left, width) = DateLabelBounds(tick, graph, xPositions, font, paint, scale);
                if (left < previousRight + FontSize * 0.5f * scale)
                {
                    return false;
                }

                previousRight = left + width;
            }

            return true;
        }

        var ticks = GraphTicks.Plan(graph.Points, graph.Culture, graph.Interval, currentYear, Fits);
        if (ticks.Any(a => !font.ContainsGlyphs(a.Label)))
        {
            ticks = GraphTicks.Plan(graph.Points, CultureInfo.InvariantCulture, graph.Interval, currentYear, Fits);
        }

        return ticks;
    }

    private static (float Left, float Width) DateLabelBounds(GraphTick tick, LineGraph graph, float[] xPositions,
        SKFont font, SKPaint paint, float scale)
    {
        var margin = EdgeMargin * scale;
        var width = font.MeasureText(tick.Label, paint);
        var left = Math.Clamp(xPositions[tick.Index] - width / 2, margin,
            Math.Max(margin, graph.Width - margin - width));

        return (left, width);
    }

    private static void DrawDateLabels(SKCanvas canvas, LineGraph graph, List<GraphTick> ticks, float[] xPositions,
        float plotBottom, float scale, SKFont font, SKPaint labelPaint)
    {
        var minGap = FontSize * 0.5f * scale;
        var previousRight = float.MinValue;

        using var tickPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            Color = AxisColor.WithAlpha(160)
        };

        foreach (var tick in ticks)
        {
            if (string.IsNullOrWhiteSpace(tick.Label))
            {
                continue;
            }

            var x = xPositions[tick.Index];
            var (left, labelWidth) = DateLabelBounds(tick, graph, xPositions, font, labelPaint, scale);

            if (left < previousRight + minGap)
            {
                continue;
            }

            canvas.DrawLine(x, plotBottom, x, plotBottom + TickLength * scale, tickPaint);
            canvas.DrawShapedText(tick.Label, left, plotBottom + LabelBaseline * scale, SKTextAlign.Left, font,
                labelPaint);

            previousRight = left + labelWidth;
        }
    }

    private static (double Min, double Max, double Step, string[] Labels, float PlotLeft) LayoutYAxis(
        LineGraph graph, SKFont font, SKPaint paint, double dataMin, double dataMax, int maxTicks, float scale)
    {
        var axis = NiceAxis(dataMin, dataMax, maxTicks, graph.ZeroBased, graph.IntegerValues);
        var tickCount = (int)Math.Round((axis.Max - axis.Min) / axis.Step) + 1;
        var unit = AxisUnit(axis.Max, axis.Step);
        var labels = new string[tickCount];
        var widestLabel = 0f;
        for (var i = 0; i < tickCount; i++)
        {
            var value = axis.Min + i * axis.Step;
            labels[i] = value == 0
                ? FormatValue(graph, 0)
                : FormatValue(graph, value / unit.Divisor) + unit.Suffix;
            widestLabel = Math.Max(widestLabel, font.MeasureText(labels[i], paint));
        }

        return (axis.Min, axis.Max, axis.Step, labels, (EdgeMargin + LabelSpacing) * scale + widestLabel);
    }

    private static (double Min, double Max, double Step, string[] Labels, float PlotLeft) ValueOnlyAxis(
        LineGraph graph, SKFont valueFont, SKPaint paint, double dataMin, double dataMax, float plotTop,
        float plotHeight, float valueGap, float scale)
    {
        valueFont.MeasureText("0", out var digitBounds, paint);
        var headroom = Math.Max(0, (-digitBounds.Top + valueGap - plotTop) / plotHeight);
        var min = graph.ZeroBased ? Math.Min(0, dataMin) : dataMin;
        var max = Math.Max(dataMax / (1 - headroom), min + 1);

        return (min, max, 0, [], EdgeMargin * scale);
    }

    private static (double Divisor, string Suffix) AxisUnit(double max, double step)
    {
        if (max >= 1000000 && step % 1000000 == 0)
        {
            return (1000000, "M");
        }

        if (max >= 1000 && (step % 1000 == 0 || step % 2500 == 0))
        {
            return (1000, "k");
        }

        return (1, string.Empty);
    }

    private static string FormatValue(LineGraph graph, double value)
    {
        return graph.ValueLabel != null
            ? graph.ValueLabel(value)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static (double Min, double Max, double Step) NiceAxis(double dataMin, double dataMax, int maxTicks,
        bool zeroBased, bool integerValues)
    {
        var low = zeroBased ? Math.Min(0, dataMin) : dataMin;
        var high = dataMax;

        if (high <= low)
        {
            high = low + 1;
        }

        var step = NiceStep((high - low) / Math.Max(1, maxTicks - 1));
        if (integerValues && step < 1)
        {
            step = 1;
        }

        var min = Math.Floor(low / step) * step;
        var max = Math.Ceiling(high / step) * step;

        if (max <= min)
        {
            max = min + step;
        }

        return (min, max, step);
    }

    private static double NiceStep(double rawStep)
    {
        if (rawStep <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var fraction = rawStep / magnitude;

        var niceFraction = fraction switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 when magnitude >= 10 => 2.5,
            <= 5 => 5,
            _ => 10
        };

        return niceFraction * magnitude;
    }
}
