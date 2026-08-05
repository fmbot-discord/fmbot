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
    public static async Task<MediaGalleryProperties> BuildPlayHistoryGraph(this GraphService graphService,
        ContextModel context, ResponseModel response, ICollection<DateTime> timestamps, string fileName,
        GraphInterval? fixedInterval = null)
    {
        var graph = graphService.RenderPlayHistory(timestamps,
            context.Localizer.Language.GetCultureInfo(),
            await GetLineColor(context),
            value => value.Format(context.NumberFormat),
            fixedInterval);

        if (graph == null)
        {
            return null;
        }

        var description = context.Localize(graph.Interval switch
        {
            GraphInterval.Year => "shared.playsPerYear",
            GraphInterval.Month => "shared.playsPerMonth",
            GraphInterval.Week => "shared.playsPerWeek",
            _ => "shared.playsPerDay"
        });

        response.Stream = graph.Image;
        response.FileName = fileName;
        response.FileDescription = description;

        return new MediaGalleryProperties
        {
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{fileName}"))
            {
                Description = description
            }
        };
    }

    private static async Task<SKColor> GetLineColor(ContextModel context)
    {
        var accentColor = await UserService.GetCustomAccentColor(context.ContextUser, context.DiscordGuild);

        return accentColor.HasValue
            ? new SKColor(accentColor.Value.Red, accentColor.Value.Green, accentColor.Value.Blue)
            : GraphColors.FmbotBlue;
    }
}
