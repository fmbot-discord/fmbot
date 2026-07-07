using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using System.Collections.Generic;

namespace FMBot.Bot.Services.Guild.Renderers;

public class ServerArtistsRenderer(PlayService playService, WhoKnowsArtistService whoKnowsArtistService)
    : IAutopostRenderer
{
    public AutopostType Type => AutopostType.ServerArtists;

    public async Task<AutopostRenderResult> RenderAsync(AutopostRenderContext context)
    {
        var allTime = context.Autopost.TimePeriod == TimePeriod.AllTime;

        List<GuildArtist> topArtists;
        if (allTime)
        {
            topArtists = (await whoKnowsArtistService.GetTopAllTimeArtistsForGuild(context.Autopost.GuildId,
                OrderType.Listeners, AutopostRendering.SnapshotEntryLimit, context.RoleUserIds)).ToList();
        }
        else
        {
            topArtists = await playService.GetGuildTopArtistsPlays(context.Autopost.GuildId, context.PeriodStart,
                OrderType.Listeners, context.PeriodEnd, AutopostRendering.SnapshotEntryLimit, context.RoleUserIds);
        }

        if (topArtists.Count == 0)
        {
            return null;
        }

        var snapshot = new AutopostSnapshot
        {
            Type = this.Type,
            Sections = [AutopostRendering.ToArtistSection(topArtists, "Top artists")]
        };

        return new AutopostRenderResult
        {
            Container = AutopostRendering.BuildChartContainer(context, snapshot, "top artists", allTime),
            Snapshot = snapshot,
            Footer = AutopostRendering.GetNextPostFooter(context, "Next post"),
            HasMoreEntries = snapshot.Sections[0].Entries.Count >
                             AutopostRendering.GetInlineCount(context.Autopost.ContentSize)
        };
    }
}
