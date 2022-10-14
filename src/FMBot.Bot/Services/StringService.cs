using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using Discord;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services;

public static class StringService
{
    public static string TrackToLinkedString(RecentTrack track, bool? rymEnabled = null)
    {
        if (!string.IsNullOrWhiteSpace(track.AlbumName))
        {
            if (rymEnabled == true)
            {
                var albumQueryName = track.AlbumName.Replace(" - Single", "");
                albumQueryName = albumQueryName.Replace(" - EP", "");

                var albumRymUrl = @"https://rateyourmusic.com/search?searchterm=";
                albumRymUrl += HttpUtility.UrlEncode($"{track.ArtistName} {albumQueryName}");
                albumRymUrl += "&searchtype=l";

                return $"[{Format.Sanitize(track.TrackName)}]({track.TrackUrl})\n" +
                       $"By **{Format.Sanitize(track.ArtistName)}**" +
                       $" | *[{Format.Sanitize(track.AlbumName)}]({albumRymUrl})*\n";
            }

            return $"[{Format.Sanitize(track.TrackName)}]({track.TrackUrl})\n" +
                   $"By **{Format.Sanitize(track.ArtistName)}**" +
                   $" | *{Format.Sanitize(track.AlbumName)}*\n";
        }

        return $"[{Format.Sanitize(track.TrackName)}]({track.TrackUrl})\n" +
               $"By **{Format.Sanitize(track.ArtistName)}**\n";
    }

    public static string TrackToString(RecentTrack track)
    {
        return $"{Format.Sanitize(track.TrackName)}\n" +
               $"By **{Format.Sanitize(track.ArtistName)}**" +
               (string.IsNullOrWhiteSpace(track.AlbumName)
                   ? "\n"
                   : $" | *{Format.Sanitize(track.AlbumName)}*\n");
    }

    public record BillboardLine(string Text, string Name, int PositionsMoved, int NewPosition, int? OldPosition);

    public static BillboardLine GetBillboardLine(string name, int newPosition, int? oldPosition, bool counter = true)
    {
        var line = new StringBuilder();

        var positionsMoved = 0;

        if (oldPosition != null)
        {
            positionsMoved = oldPosition.Value - newPosition;

            if (oldPosition < newPosition)
            {
                if ((Math.Abs(oldPosition.Value - newPosition)) < 5)
                {
                    line.Append($"<:1_to_5_down:912085138245029888>");

                }
                else
                {
                    line.Append($"<:5_or_more_down:912380324753838140>");
                }
            }
            else if (oldPosition > newPosition)
            {
                if ((Math.Abs(oldPosition.Value - newPosition)) < 5)
                {
                    line.Append($"<:1_to_5_up:912085138232442920>");
                }
                else
                {
                    line.Append($"<:5_or_more_up:912380324841918504>");
                }

            }
            else
            {
                line.Append($"<:same_position:912374491752046592>");
            }

            line.Append(" ");
        }
        else
        {
            line.Append($"<:new:912087988001980446> ");
        }

        if (counter)
        {
            line.Append($"{newPosition + 1}. ");
        }

        line.Append(name);

        return new BillboardLine(line.ToString(), name, positionsMoved, newPosition + 1, oldPosition + 1);
    }

    public static string GetBillBoardSettingString(TimeSettingsModel timeSettings,
        DateTime? userSettingsRegisteredLastFm)
    {
        if (timeSettings.BillboardTimeDescription != null)
        {
            return $"Billboard mode enabled - Comparing to {timeSettings.BillboardTimeDescription}";
        }
        if (timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var startDateTime = timeSettings.BillboardStartDateTime.Value;

            if (userSettingsRegisteredLastFm.HasValue && startDateTime < userSettingsRegisteredLastFm.Value)
            {
                startDateTime = userSettingsRegisteredLastFm.Value;
            }

            if (timeSettings.BillboardStartDateTime.Value.Year == DateTime.UtcNow.Year)
            {
                return $"Billboard mode enabled - Comparing to {startDateTime:MMM dd} til {timeSettings.BillboardEndDateTime.Value:MMM dd}";

            }

            return $"Billboard mode enabled - Comparing to {startDateTime:MMM dd yyyy} til {timeSettings.BillboardEndDateTime.Value:MMM dd yyyy}";
        }

        return null;
    }

    public static StaticPaginator BuildStaticPaginator(IList<PageBuilder> pages)
    {
        var builder = new StaticPaginatorBuilder()
            .WithPages(pages)
            .WithFooter(PaginatorFooter.None)
            .WithActionOnTimeout(ActionOnStop.DeleteInput);

        if (pages.Count != 1)
        {
            builder.WithOptions(DiscordConstants.PaginationEmotes);
        }

        return builder.Build();
    }

    public static StaticPaginator BuildSimpleStaticPaginator(IEnumerable<PageBuilder> pages)
    {
        var builder = new StaticPaginatorBuilder()
            .WithPages(pages)
            .WithFooter(PaginatorFooter.None)
            .WithActionOnTimeout(ActionOnStop.DeleteInput);

        builder.WithOptions(new Dictionary<IEmote, PaginatorAction>
        {
            { Emote.Parse("<:pages_previous:883825508507336704>"), PaginatorAction.Backward},
            { Emote.Parse("<:pages_next:883825508087922739>"), PaginatorAction.Forward},
        });

        return builder.Build();
    }
}
