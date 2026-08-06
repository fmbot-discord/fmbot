using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Images.Models;
using NetCord.Rest;
using SkiaSharp;

namespace FMBot.Bot.Extensions;

public static class GraphExtensions
{
    public const int DefaultGraphHeight = 165;
    public const int CompactGraphHeight = 115;

    public static async Task<MediaGalleryProperties> BuildPlayHistoryGraph(this GraphService graphService,
        ContextModel context, ResponseModel response, IReadOnlyList<DayPlayCount> dailyPlays, string fileName,
        GraphInterval? fixedInterval = null, int height = DefaultGraphHeight, DateTime? windowFrom = null,
        DateTime? windowUntil = null)
    {
        if (dailyPlays == null || dailyPlays.Count == 0)
        {
            return null;
        }

        var points = new List<GraphPoint>(dailyPlays.Count);
        foreach (var day in dailyPlays)
        {
            points.Add(new GraphPoint
            {
                Date = day.Day,
                Value = day.Plays
            });
        }

        var graph = graphService.RenderPlayHistory(points,
            context.Localizer.Language.GetCultureInfo(),
            await GetLineColor(context, response),
            value => value.Format(context.NumberFormat),
            fixedInterval,
            windowFrom,
            windowUntil,
            height: height);

        return AttachGraph(response, graph, fileName);
    }

    private static MediaGalleryProperties AttachGraph(ResponseModel response, PlayHistoryGraph graph, string fileName)
    {
        if (graph == null)
        {
            return null;
        }

        response.Stream = graph.Image;
        response.FileName = fileName;

        return
        [
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{fileName}"))
        ];
    }

    private const float MinimumLineLightness = 40f;

    private static async Task<SKColor> GetLineColor(ContextModel context, ResponseModel response)
    {
        var accentColor = response.ComponentsContainer?.AccentColor ??
                          await UserService.GetCustomAccentColor(context.ContextUser, context.DiscordGuild);

        return accentColor.HasValue
            ? Brighten(new SKColor(accentColor.Value.Red, accentColor.Value.Green, accentColor.Value.Blue))
            : GraphColors.FmbotBlue;
    }

    private static SKColor Brighten(SKColor color)
    {
        color.ToHsl(out var hue, out var saturation, out var lightness);

        return lightness >= MinimumLineLightness
            ? color
            : SKColor.FromHsl(hue, saturation, MinimumLineLightness);
    }
}
