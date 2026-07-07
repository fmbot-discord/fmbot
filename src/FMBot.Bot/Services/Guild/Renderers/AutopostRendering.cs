using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord.Rest;

namespace FMBot.Bot.Services.Guild.Renderers;

public static class AutopostRendering
{
    public const int SnapshotEntryLimit = 100;

    public static int GetInlineCount(AutopostSize size)
    {
        return size switch
        {
            AutopostSize.Compact => 5,
            AutopostSize.Detailed => 20,
            _ => 10
        };
    }

    public static int GetSecondaryInlineCount(AutopostSize size)
    {
        return size switch
        {
            AutopostSize.Compact => 3,
            AutopostSize.Detailed => 10,
            _ => 5
        };
    }

    public static string GetScheduleDisplay(AutopostSchedule schedule)
    {
        return schedule == AutopostSchedule.Monthly ? "Monthly" : "Weekly";
    }

    public static string GetPeriodDisplay(AutopostSchedule schedule, DateTime periodStart, DateTime periodEnd)
    {
        return schedule == AutopostSchedule.Monthly
            ? periodStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : $"<t:{((DateTimeOffset)periodStart).ToUnixTimeSeconds()}:D> to <t:{((DateTimeOffset)periodEnd).ToUnixTimeSeconds()}:D>";
    }

    public static int? GetPreviousPosition(AutopostSnapshotSection previousSection, AutopostSnapshotEntry entry)
    {
        if (previousSection == null)
        {
            return null;
        }

        for (var i = 0; i < previousSection.Entries.Count; i++)
        {
            if (SameEntity(entry, previousSection.Entries[i]))
            {
                return i;
            }
        }

        return null;
    }

    private static bool SameEntity(AutopostSnapshotEntry first, AutopostSnapshotEntry second)
    {
        if (first.TrackId.HasValue && second.TrackId.HasValue)
        {
            return first.TrackId == second.TrackId;
        }

        if (first.AlbumId.HasValue && second.AlbumId.HasValue)
        {
            return first.AlbumId == second.AlbumId;
        }

        if (first.ArtistId.HasValue && second.ArtistId.HasValue)
        {
            return first.ArtistId == second.ArtistId;
        }

        return string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(first.ArtistName, second.ArtistName, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatEntryLine(AutopostEntityType entityType, AutopostSnapshotEntry entry)
    {
        if (entityType == AutopostEntityType.Artist)
        {
            return $"**{StringExtensions.Sanitize(StringExtensions.TruncateLongString(entry.Name, 90))}** · " +
                   $"*{entry.Listeners:n0} {StringExtensions.GetListenersString(entry.Listeners)}* · " +
                   $"{entry.Playcount:n0} {StringExtensions.GetPlaysString(entry.Playcount)}";
        }

        return $"**{StringExtensions.Sanitize(StringExtensions.TruncateLongString(entry.Name, 60))}** " +
               $"by {StringExtensions.Sanitize(StringExtensions.TruncateLongString(entry.ArtistName, 40))} · " +
               $"*{entry.Listeners:n0} {StringExtensions.GetListenersString(entry.Listeners)}*";
    }

    public static string BuildSectionDisplay(AutopostSnapshotSection section, AutopostSnapshot previousSnapshot,
        int inlineCount, bool billboard, bool includeTitle = true)
    {
        var lines = new StringBuilder();

        if (includeTitle)
        {
            lines.AppendLine($"**{section.Title}**");
        }

        var previousSection = billboard
            ? previousSnapshot?.Sections?.FirstOrDefault(f => f.EntityType == section.EntityType)
            : null;

        foreach (var entry in section.Entries.Take(inlineCount))
        {
            var line = FormatEntryLine(section.EntityType, entry);

            if (previousSection != null)
            {
                var previousPosition = GetPreviousPosition(previousSection, entry);
                lines.AppendLine(StringService.GetBillboardLine(line, entry.Rank - 1, previousPosition).Text);
            }
            else
            {
                lines.AppendLine($"{entry.Rank}. {line}");
            }
        }

        return lines.ToString();
    }

    public static AutopostSnapshotSection ToArtistSection(IEnumerable<GuildArtist> artists, string title)
    {
        return new AutopostSnapshotSection
        {
            EntityType = AutopostEntityType.Artist,
            Title = title,
            Entries = artists
                .Take(SnapshotEntryLimit)
                .Select((artist, index) => new AutopostSnapshotEntry
                {
                    Rank = index + 1,
                    ArtistId = artist.ArtistId,
                    Name = artist.ArtistName,
                    ArtistName = artist.ArtistName,
                    Playcount = artist.TotalPlaycount,
                    Listeners = artist.ListenerCount
                })
                .ToList()
        };
    }

    public static AutopostSnapshotSection ToAlbumSection(IEnumerable<GuildAlbum> albums, string title,
        AutopostEntityType entityType = AutopostEntityType.Album)
    {
        return new AutopostSnapshotSection
        {
            EntityType = entityType,
            Title = title,
            Entries = albums
                .Take(SnapshotEntryLimit)
                .Select((album, index) => new AutopostSnapshotEntry
                {
                    Rank = index + 1,
                    AlbumId = album.AlbumId,
                    Name = album.AlbumName,
                    ArtistName = album.ArtistName,
                    Playcount = album.TotalPlaycount,
                    Listeners = album.ListenerCount
                })
                .ToList()
        };
    }

    public static AutopostSnapshotSection ToTrackSection(IEnumerable<GuildTrack> tracks, string title)
    {
        return new AutopostSnapshotSection
        {
            EntityType = AutopostEntityType.Track,
            Title = title,
            Entries = tracks
                .Take(SnapshotEntryLimit)
                .Select((track, index) => new AutopostSnapshotEntry
                {
                    Rank = index + 1,
                    TrackId = track.TrackId,
                    Name = track.TrackName,
                    ArtistName = track.ArtistName,
                    Playcount = track.TotalPlaycount,
                    Listeners = track.ListenerCount
                })
                .ToList()
        };
    }

    public static ComponentContainerProperties BuildChartContainer(AutopostRenderContext context,
        AutopostSnapshot snapshot, string typeLabel, bool allTime)
    {
        var container = new ComponentContainerProperties
        {
            AccentColor = DiscordConstants.InformationColorBlue
        };

        var header = new StringBuilder();
        var artistFilter = !string.IsNullOrWhiteSpace(context.Autopost.ArtistFilter)
            ? $" {StringExtensions.Sanitize(context.Autopost.ArtistFilter)}"
            : "";
        header.AppendLine(allTime
            ? $"## 📊 All-time{artistFilter} {typeLabel} for {StringExtensions.Sanitize(context.GuildName)}"
            : $"## 📊 {GetScheduleDisplay(context.Autopost.Schedule)}{artistFilter} {typeLabel} for {StringExtensions.Sanitize(context.GuildName)}");

        var subtitle = new StringBuilder();
        if (!allTime)
        {
            subtitle.Append(GetPeriodDisplay(context.Autopost.Schedule, context.PeriodStart, context.PeriodEnd));
        }

        if (context.Autopost.RoleIds is { Length: > 0 })
        {
            if (subtitle.Length > 0)
            {
                subtitle.Append(" · ");
            }

            subtitle.Append($"Filtered to {GetRoleMentions(context.Autopost.RoleIds)}");
        }

        header.Append(subtitle);

        container.AddComponent(new TextDisplayProperties(header.ToString().TrimEnd()));
        container.AddComponent(new ComponentSeparatorProperties());

        var inlineCount = GetInlineCount(context.Autopost.ContentSize);
        container.AddComponent(new TextDisplayProperties(
            BuildSectionDisplay(snapshot.Sections[0], context.PreviousSnapshot, inlineCount, true, false).TrimEnd()));

        return container;
    }

    public static string GetNextPostFooter(AutopostRenderContext context, string description)
    {
        return context.NextPost.HasValue
            ? $"-# ✨ {description} <t:{((DateTimeOffset)context.NextPost.Value).ToUnixTimeSeconds()}:R>"
            : null;
    }

    public static string GetRoleMentions(ulong[] roleIds)
    {
        return string.Join(", ", roleIds.Select(s => $"<@&{s}>"));
    }
}
