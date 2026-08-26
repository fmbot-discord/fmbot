using System.Globalization;
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
    private const double MinimumPlays = 5;
    private const int MaxDateTicks = 9;

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
        DateTime? windowFrom = null, DateTime? windowUntil = null, int width = 660, int height = 165)
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

        var interval = fixedInterval ?? GraphSeries.PickInterval(earliest, until,
            totalPlays >= int.MaxValue ? int.MaxValue : (int)totalPlays);

        if (!windowFrom.HasValue)
        {
            var earliestStart = GraphSeries.EarliestStart(until, interval);
            earliest = earliest < earliestStart ? earliest : earliestStart;
        }

        var from = GraphSeries.LimitToMaxPoints(earliest, until, interval);

        if (interval != GraphInterval.Day)
        {
            (from, until) = GraphSeries.TrimToWholeIntervals(from, until, interval);
        }

        var points = GraphSeries.FromDailyCounts(dailyPlays, interval, from, until);

        if (!windowFrom.HasValue)
        {
            TrimLeadingEmpty(points, GraphSeries.EarliestStart(until, interval));
        }

        if (points.Count < 3 || points.Count(w => w.Value > 0) < 2 || points.Sum(s => s.Value) < MinimumPlays)
        {
            return null;
        }

        var typeface = GetTypeface();
        var ticks = GraphTicks.Plan(points, MaxDateTicks, culture);
        if (ticks.Any(a => !typeface.ContainsGlyphs(a.Label)))
        {
            ticks = GraphTicks.Plan(points, MaxDateTicks, CultureInfo.InvariantCulture);
        }

        var image = RenderLineGraph(new LineGraph
        {
            Points = points,
            Width = width,
            Height = height,
            LineColor = lineColor,
            ValueLabel = valueLabel,
            Ticks = ticks
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

        var axis = NiceAxis(dataMin, dataMax, graph.MaxYTicks, graph.ZeroBased, graph.IntegerValues);
        var scale = graph.Width / BaseWidth;

        using var font = new SKFont(GetTypeface())
        {
            Size = FontSize * scale,
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = LabelColor
        };

        var tickCount = (int)Math.Round((axis.Max - axis.Min) / axis.Step) + 1;
        var unit = AxisUnit(axis.Max, axis.Step);
        var valueLabels = new string[tickCount];
        var widestLabel = 0f;
        for (var i = 0; i < tickCount; i++)
        {
            var value = axis.Min + i * axis.Step;
            valueLabels[i] = value == 0
                ? FormatValue(graph, 0)
                : FormatValue(graph, value / unit.Divisor) + unit.Suffix;
            widestLabel = Math.Max(widestLabel, font.MeasureText(valueLabels[i], labelPaint));
        }

        var plotLeft = (EdgeMargin + LabelSpacing) * scale + widestLabel;
        var plotRight = graph.Width - PaddingRight * scale;
        var plotTop = PaddingTop * scale;
        var plotBottom = graph.Height - AxisHeight * scale;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;

        if (plotWidth < 60 || plotHeight < 30)
        {
            return null;
        }

        var range = axis.Max - axis.Min;
        var xPositions = new float[graph.Points.Count];
        var yPositions = new float[graph.Points.Count];
        for (var i = 0; i < graph.Points.Count; i++)
        {
            xPositions[i] = plotLeft + (float)i / (graph.Points.Count - 1) * plotWidth;
            yPositions[i] = plotTop + (float)(1 - (graph.Points[i].Value - axis.Min) / range) * plotHeight;
        }

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

        for (var i = 0; i < tickCount; i++)
        {
            var y = plotTop + (float)(1 - i * axis.Step / range) * plotHeight;

            canvas.DrawLine(plotLeft, y, plotRight, y, gridPaint);
            canvas.DrawShapedText(valueLabels[i], plotLeft - LabelSpacing * scale, y + LabelCenter * scale,
                SKTextAlign.Right, font, labelPaint);
        }

        canvas.DrawLine(plotLeft, plotTop, plotLeft, plotBottom, axisPaint);
        canvas.DrawLine(plotLeft, plotBottom, plotRight, plotBottom, axisPaint);

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

        DrawDateLabels(canvas, graph, xPositions, plotBottom, scale, font, labelPaint);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;

        return stream;
    }

    private static void DrawDateLabels(SKCanvas canvas, LineGraph graph, float[] xPositions, float plotBottom,
        float scale, SKFont font, SKPaint labelPaint)
    {
        var margin = EdgeMargin * scale;
        var minGap = FontSize * 0.5f * scale;
        var previousRight = float.MinValue;

        using var tickPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            Color = AxisColor.WithAlpha(160)
        };

        foreach (var tick in graph.Ticks)
        {
            if (string.IsNullOrWhiteSpace(tick.Label))
            {
                continue;
            }

            var x = xPositions[tick.Index];
            var labelWidth = font.MeasureText(tick.Label, labelPaint);
            var left = Math.Clamp(x - labelWidth / 2, margin, Math.Max(margin, graph.Width - margin - labelWidth));

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

    private static (double Divisor, string Suffix) AxisUnit(double max, double step)
    {
        if (max >= 1000000 && step % 1000000 == 0)
        {
            return (1000000, "M");
        }

        if (max >= 1000 && step % 1000 == 0)
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
            <= 5 => 5,
            _ => 10
        };

        return niceFraction * magnitude;
    }
}
