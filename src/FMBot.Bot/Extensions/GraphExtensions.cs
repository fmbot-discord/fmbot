using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Images.Models;
using FMBot.Persistence.Domain.Models;
using NetCord.Rest;
using SkiaSharp;

namespace FMBot.Bot.Extensions;

public static class GraphExtensions
{
    private const int DefaultGraphHeight = 165;
    public const int CompactGraphHeight = 110;

    public static async Task<MediaGalleryProperties> BuildPlayHistoryGraph(this GraphService graphService,
        ContextModel context, ResponseModel response, IReadOnlyList<DayPlayCount> dailyPlays, string fileName,
        GraphInterval? fixedInterval = null, int height = DefaultGraphHeight, DateTime? windowFrom = null,
        DateTime? windowUntil = null, string windowTimeZone = null)
    {
        if (dailyPlays == null || dailyPlays.Count == 0)
        {
            return null;
        }

        var graphType = context.GraphType ?? GraphType.Bar;
        if (graphType == GraphType.Off)
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
            ToWindowStart(windowFrom, windowTimeZone),
            ToWindowEnd(windowUntil, windowTimeZone),
            height: height,
            style: graphType);

        return AttachGraph(response, graph, fileName);
    }

    public static MediaGalleryProperties BuildImportGraph(this GraphService graphService, ContextModel context,
        ResponseModel response, IEnumerable<UserPlay> plays, DataSource dataSource, string fileName)
    {
        if (plays == null || context.GraphType == GraphType.Off)
        {
            return null;
        }

        var importDays = new Dictionary<DateTime, double>();
        var lastFmDays = new Dictionary<DateTime, double>();
        foreach (var play in plays)
        {
            var days = play.PlaySource == PlaySource.LastFm ? lastFmDays : importDays;
            days.TryGetValue(play.TimePlayed.Date, out var count);
            days[play.TimePlayed.Date] = count + 1;
        }

        if (importDays.Count == 0)
        {
            return null;
        }

        var graph = graphService.RenderStackedPlayHistory(
            ToGraphPoints(importDays),
            ToGraphPoints(lastFmDays),
            new GraphLegendItem("Imports", GraphColors.FmbotBlue),
            new GraphLegendItem("Last.fm", GraphColors.LastFmRed),
            context.Localizer.Language.GetCultureInfo(),
            value => value.Format(context.NumberFormat),
            dataSource.GetAttribute<OptionAttribute>().Name);

        return AttachGraph(response, graph, fileName);
    }

    private static List<GraphPoint> ToGraphPoints(Dictionary<DateTime, double> days)
    {
        return days
            .OrderBy(o => o.Key)
            .Select(s => new GraphPoint
            {
                Date = s.Key,
                Value = s.Value
            })
            .ToList();
    }

    private static DateTime? ToWindowStart(DateTime? start, string timeZone)
    {
        if (!start.HasValue)
        {
            return null;
        }

        var localStart = ToLocalTime(start.Value, timeZone);
        var nearestMidnight = localStart.AddHours(12).Date;

        return (localStart - nearestMidnight).Duration() <= TimeSpan.FromHours(1)
            ? DateTime.SpecifyKind(nearestMidnight, DateTimeKind.Utc)
            : start;
    }

    private static DateTime? ToWindowEnd(DateTime? end, string timeZone)
    {
        if (!end.HasValue || end.Value >= DateTime.UtcNow.AddHours(-1))
        {
            return end;
        }

        return DateTime.SpecifyKind(ToLocalTime(end.Value, timeZone).AddHours(-12).Date, DateTimeKind.Utc);
    }

    private static DateTime ToLocalTime(DateTime utcTime, string timeZone)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc),
            SettingService.ResolveTimeZone(timeZone));
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
        var user = context.ContextUser;

        switch (user?.GraphColor ?? GraphColor.EmbedColor)
        {
            case GraphColor.FmbotBlue:
                return GraphColors.FmbotBlue;
            case GraphColor.Custom when ColorExtensions.TryParseHexColor(user.GraphCustomColor, out var customColor):
                return Brighten(ToSkColor(customColor));
            case GraphColor.RoleColor:
                var roleColor = await UserService.GetRoleColor(user, context.DiscordGuild);
                if (roleColor.HasValue)
                {
                    return Brighten(ToSkColor(roleColor.Value));
                }

                break;
        }

        var accentColor = response.ComponentsContainer?.AccentColor ??
                          await UserService.GetCustomAccentColor(user, context.DiscordGuild);

        return accentColor.HasValue
            ? Brighten(ToSkColor(accentColor.Value))
            : GraphColors.FmbotBlue;
    }

    private static SKColor ToSkColor(NetCord.Color color)
    {
        return new SKColor(color.Red, color.Green, color.Blue);
    }

    private static SKColor Brighten(SKColor color)
    {
        color.ToHsl(out var hue, out var saturation, out var lightness);

        return lightness >= MinimumLineLightness
            ? color
            : SKColor.FromHsl(hue, saturation, MinimumLineLightness);
    }
}
