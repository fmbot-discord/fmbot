using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Rest;

namespace FMBot.Bot.Services.Guild.Renderers;

public class ServerRecapRenderer(PlayService playService, IServiceProvider serviceProvider) : IAutopostRenderer
{
    public AutopostType Type => AutopostType.ServerRecap;

    public async Task<AutopostRenderResult> RenderAsync(AutopostRenderContext context)
    {
        var guildId = context.Autopost.GuildId;

        var statsTask = playService.GetGuildPlayStats(guildId, context.PeriodStart, context.PeriodEnd,
            context.RoleUserIds);
        var topArtistsTask = playService.GetGuildTopArtistsPlays(guildId, context.PeriodStart,
            OrderType.Listeners, context.PeriodEnd, userIds: context.RoleUserIds);
        var topAlbumsTask = playService.GetGuildTopAlbumsPlays(guildId, context.PeriodStart,
            OrderType.Listeners, null, context.PeriodEnd, 400, context.RoleUserIds);
        var topTracksTask = playService.GetGuildTopTracksPlays(guildId, context.PeriodStart,
            OrderType.Listeners, null, context.PeriodEnd, userIds: context.RoleUserIds);

        var stats = await statsTask;

        if (stats == null || stats.TotalPlaycount == 0)
        {
            return null;
        }

        var topArtists = await topArtistsTask;
        var topAlbums = await topAlbumsTask;
        var topTracks = await topTracksTask;

        var albumService = serviceProvider.GetRequiredService<AlbumService>();
        var releaseWindowStart = context.Autopost.Schedule == AutopostSchedule.Weekly
            ? context.PeriodEnd.AddDays(-28)
            : context.PeriodStart;
        var newReleases = await albumService.FilterAlbumsToReleasePeriod(topAlbums, releaseWindowStart,
            context.PeriodEnd);
        topAlbums = await albumService.FilterGuildAlbumsThatAreSingles(topAlbums);

        if (topArtists.Count == 0)
        {
            return null;
        }

        var snapshot = new AutopostSnapshot
        {
            Type = this.Type,
            TotalPlaycount = stats.TotalPlaycount,
            ListenerCount = stats.ListenerCount
        };

        snapshot.Sections.Add(AutopostRendering.ToArtistSection(topArtists, "Top artists"));

        if (topAlbums.Count > 0)
        {
            snapshot.Sections.Add(AutopostRendering.ToAlbumSection(topAlbums, "Top albums"));
        }

        if (newReleases.Count > 0)
        {
            snapshot.Sections.Add(AutopostRendering.ToAlbumSection(newReleases, "Popular new releases",
                AutopostEntityType.NewRelease));
        }

        if (topTracks.Count > 0)
        {
            snapshot.Sections.Add(AutopostRendering.ToTrackSection(topTracks, "Top tracks"));
        }

        var container = BuildContainer(context, snapshot);

        return new AutopostRenderResult
        {
            Container = container,
            Snapshot = snapshot,
            Footer = AutopostRendering.GetNextPostFooter(context, "Next server recap"),
            HasMoreEntries = snapshot.Sections.Any(s => s.Entries.Count >
                                                        (s.EntityType == AutopostEntityType.Artist
                                                            ? AutopostRendering.GetInlineCount(context.Autopost.ContentSize)
                                                            : AutopostRendering.GetSecondaryInlineCount(context.Autopost.ContentSize)))
        };
    }

    private static ComponentContainerProperties BuildContainer(AutopostRenderContext context,
        AutopostSnapshot snapshot)
    {
        var container = new ComponentContainerProperties
        {
            AccentColor = DiscordConstants.InformationColorBlue
        };

        var header = new StringBuilder();
        header.AppendLine(
            $"## 📊 {AutopostRendering.GetScheduleDisplay(context.Autopost.Schedule)} recap for {StringExtensions.Sanitize(context.GuildName)}");

        var subtitle = new StringBuilder();
        subtitle.Append(AutopostRendering.GetPeriodDisplay(context.Autopost.Schedule, context.PeriodStart,
            context.PeriodEnd));
        subtitle.Append($" · **{snapshot.TotalPlaycount:n0}** {StringExtensions.GetPlaysString(snapshot.TotalPlaycount)} " +
                        $"from **{snapshot.ListenerCount:n0}** {StringExtensions.GetListenersString(snapshot.ListenerCount)}");

        if (context.Autopost.RoleIds is { Length: > 0 })
        {
            subtitle.Append($" · Filtered to {AutopostRendering.GetRoleMentions(context.Autopost.RoleIds)}");
        }

        header.Append(subtitle);

        container.AddComponent(new TextDisplayProperties(header.ToString()));

        foreach (var section in snapshot.Sections)
        {
            container.AddComponent(new ComponentSeparatorProperties());

            var inlineCount = section.EntityType == AutopostEntityType.Artist
                ? AutopostRendering.GetInlineCount(context.Autopost.ContentSize)
                : AutopostRendering.GetSecondaryInlineCount(context.Autopost.ContentSize);

            var billboard = section.EntityType != AutopostEntityType.NewRelease;

            container.AddComponent(new TextDisplayProperties(
                AutopostRendering.BuildSectionDisplay(section, context.PreviousSnapshot, inlineCount, billboard)
                    .TrimEnd()));
        }

        return container;
    }
}
