using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services.Guild.Renderers;

public class ServerTracksRenderer(PlayService playService, WhoKnowsTrackService whoKnowsTrackService)
    : IAutopostRenderer
{
    public AutopostType Type => AutopostType.ServerTracks;

    public async Task<AutopostRenderResult> RenderAsync(AutopostRenderContext context)
    {
        var allTime = context.Autopost.TimePeriod == TimePeriod.AllTime;

        List<GuildTrack> topTracks;
        if (allTime)
        {
            topTracks = (await whoKnowsTrackService.GetTopAllTimeTracksForGuild(context.Autopost.GuildId,
                OrderType.Listeners, context.Autopost.ArtistFilter, context.RoleUserIds)).ToList();
        }
        else
        {
            topTracks = await playService.GetGuildTopTracksPlays(context.Autopost.GuildId, context.PeriodStart,
                OrderType.Listeners, context.Autopost.ArtistFilter, context.PeriodEnd,
                AutopostRendering.SnapshotEntryLimit, context.RoleUserIds);
        }

        if (topTracks.Count == 0)
        {
            return null;
        }

        var snapshot = new AutopostSnapshot
        {
            Type = this.Type,
            Sections = [AutopostRendering.ToTrackSection(topTracks, "Top tracks")]
        };

        return new AutopostRenderResult
        {
            Container = AutopostRendering.BuildChartContainer(context, snapshot, "top tracks", allTime),
            Snapshot = snapshot,
            Footer = AutopostRendering.GetNextPostFooter(context, "Next post"),
            HasMoreEntries = snapshot.Sections[0].Entries.Count >
                             AutopostRendering.GetInlineCount(context.Autopost.ContentSize)
        };
    }
}
