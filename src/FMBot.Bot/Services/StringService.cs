using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.WebUtilities;

namespace FMBot.Bot.Services;

public static class StringService
{
    public static string TrackToLinkedString(RecentTrack track, bool? rymEnabled = null, bool bigTrackName = true)
    {
        var description = new StringBuilder();

        if (bigTrackName)
        {
            description.AppendLine($"### [{StringExtensions.Sanitize(track.TrackName)}]({track.TrackUrl})");
        }
        else
        {
            description.AppendLine($"**[{StringExtensions.Sanitize(track.TrackName)}]({track.TrackUrl})**");
        }

        description.Append($"**{StringExtensions.Sanitize(track.ArtistName)}**");

        if (!string.IsNullOrWhiteSpace(track.AlbumName))
        {
            if (rymEnabled == true)
            {
                var albumRymUrl = StringExtensions.GetRymUrl(track.AlbumName, track.ArtistName);
                
                description.Append($" • *[{StringExtensions.Sanitize(track.AlbumName)}]({albumRymUrl})*");
            }
            else
            {
                description.Append($" • *{StringExtensions.Sanitize(track.AlbumName)}*");
            }
        }

        description.AppendLine();
        return description.ToString();
    }

    public static string TrackToLinkedStringWithTimestamp(RecentTrack track, bool? rymEnabled = null, TimeSpan? trackLength = null)
    {
        var description = new StringBuilder();

        description.AppendLine($"**[{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.TrackName, 200))}]({track.TrackUrl})** by **{StringExtensions.Sanitize(track.ArtistName)}**");
        
        description.Append("-# ");

        if (!track.TimePlayed.HasValue || track.NowPlaying)
        {
            description.Append("🎶 • ");
        }
        else
        {
            var specifiedDateTime = DateTime.SpecifyKind(track.TimePlayed.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

            var format = DateTime.UtcNow.AddHours(-20) < track.TimePlayed.Value ? "t" : "f";

            description.Append($"<t:{dateValue}:{format}>");

            if (!string.IsNullOrWhiteSpace(track.AlbumName))
            {
                description.Append($" • ");
            }
        }

        if (trackLength.HasValue)
        {
            description.Append($"{StringExtensions.GetListeningTimeString(trackLength.Value, true)} - ");
        }

        if (!string.IsNullOrWhiteSpace(track.AlbumName))
        {
            description.Append("*");

            if (rymEnabled == true)
            {
                var searchTerm = track.AlbumName.Replace(" - Single", "");
                searchTerm = searchTerm.Replace(" - EP", "");
                searchTerm = $"{StringExtensions.TruncateLongString(track.ArtistName, 25)} {StringExtensions.TruncateLongString(searchTerm, 25)}";

                var url = QueryHelpers.AddQueryString("https://rateyourmusic.com/search",
                    new Dictionary<string, string>
                {
                    {"searchterm", $"{searchTerm}"},
                    {"searchtype", $"l"}
                });

                if (url.Length < 180)
                {
                    description.Append($"[{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.AlbumName, 160))}]({url})");
                }
                else
                {
                    description.Append($"{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.AlbumName, 200))}");
                }
            }
            else
            {
                description.Append($"{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.AlbumName, 200))}");
            }

            description.Append("*");
        }

        description.AppendLine();

        return description.ToString();
    }

    public static string TrackToString(RecentTrack track)
    {
        return $"**{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.TrackName, 320))}**\n" +
               $"By **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.ArtistName, 320))}**" +
               (string.IsNullOrWhiteSpace(track.AlbumName)
                   ? "\n"
                   : $" | *{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.AlbumName, 320))}*\n");
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
                    line.Append($"{DiscordConstants.OneToFiveDown}");
                }
                else
                {
                    line.Append($"{DiscordConstants.FiveOrMoreDown}");
                }
            }
            else if (oldPosition > newPosition)
            {
                if ((Math.Abs(oldPosition.Value - newPosition)) < 5)
                {
                    line.Append($"{DiscordConstants.OneToFiveUp}");
                }
                else
                {
                    line.Append($"{DiscordConstants.FiveOrMoreUp}");
                }

            }
            else
            {
                line.Append($"{DiscordConstants.SamePosition}");
            }

            line.Append(" ");
        }
        else
        {
            line.Append($"{DiscordConstants.New} ");
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

    public static void SinglePageToEmbedResponseWithButton(this ResponseModel response, PageBuilder page,
        string customOptionId = null, IEmote optionEmote = null, string optionDescription = null, SelectMenuBuilder selectMenu = null)
    {
        response.Embed.WithTitle(page.Title);
        response.Embed.WithAuthor(page.Author);
        response.Embed.WithDescription(page.Description);
        response.Embed.WithUrl(page.Url);
        response.Embed.WithThumbnailUrl(page.ThumbnailUrl);
        response.Embed.WithFooter(page.Footer);
        response.Embed.Color = null;

        if (customOptionId != null || selectMenu != null)
        {
            response.Components = new ComponentBuilder();
        }
        if (customOptionId != null)
        {
            response.Components.WithButton(customId: customOptionId, emote: optionEmote, label: optionDescription, style: ButtonStyle.Secondary);
        }
        if (selectMenu != null)
        {
            response.Components.WithSelectMenu(selectMenu);
        }
    }

    public static StaticPaginator BuildStaticPaginator(IList<PageBuilder> pages, string customOptionId = null, IEmote optionEmote = null)
    {
        var builder = new StaticPaginatorBuilder()
            .WithPages(pages)
            .WithFooter(PaginatorFooter.None)
            .WithActionOnTimeout(ActionOnStop.DeleteInput);

        if (pages.Count == 1)
        {
            builder.WithOptions(new List<IPaginatorButton> { PaginatorButton.Hidden });
        }
        else if (pages.Count != 1 || customOptionId != null)
        {
            builder.WithOptions(DiscordConstants.PaginationEmotes);
        }

        if (customOptionId != null && optionEmote != null)
        {
            builder.AddOption(customOptionId, optionEmote, null, ButtonStyle.Primary);
        }

        if (customOptionId == null && pages.Count >= 25)
        {
            builder.AddOption(new KeyValuePair<IEmote, PaginatorAction>(Emote.Parse("<:pages_goto:1138849626234036264>"), PaginatorAction.Jump));
        }

        return builder.Build();
    }

    public static StaticPaginator BuildStaticPaginatorWithSelectMenu(IList<PageBuilder> pages,
        SelectMenuBuilder selectMenuBuilder, string customOptionId = null, IEmote optionEmote = null)
    {
        var builder = new StaticPaginatorBuilder()
            .WithPages(pages)
            .WithFooter(PaginatorFooter.None)
            .WithActionOnTimeout(ActionOnStop.DeleteInput);

        if (pages.Count == 1)
        {
            builder.WithOptions(new List<IPaginatorButton> { PaginatorButton.Hidden });
        }
        else
        {
            builder.WithOptions(DiscordConstants.PaginationEmotes);
        }

        if (customOptionId != null && optionEmote != null)
        {
            builder.AddOption(customOptionId, optionEmote, null, ButtonStyle.Primary);
        }

        if (pages.Count >= 10 && customOptionId == null)
        {
            builder.AddOption(new KeyValuePair<IEmote, PaginatorAction>(Emote.Parse("<:pages_goto:1138849626234036264>"), PaginatorAction.Jump));
        }

        if (selectMenuBuilder != null)
        {
            builder.WithSelectMenus(new List<SelectMenuBuilder> { selectMenuBuilder });
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

    public static string UserDiscogsReleaseToStringWithTitle(UserDiscogsReleases discogsRelease)
    {
        var description = new StringBuilder();
        description.Append($"**");
        description.Append($"{discogsRelease.Release.Artist}");

        if (discogsRelease.Release.FeaturingArtistJoin != null && discogsRelease.Release.FeaturingArtist != null)
        {
            description.Append($"** {discogsRelease.Release.FeaturingArtistJoin} **{discogsRelease.Release.FeaturingArtist}");
        }

        description.Append($" - ");

        description.Append($"[{discogsRelease.Release.Title}]({Constants.DiscogsReleaseUrl}{discogsRelease.Release.DiscogsId})");
        description.Append("**");

        description.AppendLine();

        description.Append(GetDiscogsFormatEmote(discogsRelease.Release.Format));

        description.Append($" {discogsRelease.Release.Format}");

        if (discogsRelease.Release.FormatText != null)
        {
            description.Append($" - *{discogsRelease.Release.FormatText}*");
        }

        if (discogsRelease.Rating.HasValue)
        {
            description.Append($" - ");

            for (var i = 0; i < discogsRelease.Rating; i++)
            {
                description.Append("<:star:1043647352273121311>");
            }
        }

        description.AppendLine();

        return description.ToString();
    }

    public static string UserDiscogsReleaseToString(UserDiscogsReleases discogsRelease)
    {
        var description = new StringBuilder();

        description.Append(GetDiscogsFormatEmote(discogsRelease.Release.Format));

        description.Append($" [{discogsRelease.Release.Format}]({Constants.DiscogsReleaseUrl}{discogsRelease.Release.DiscogsId})");
        if (discogsRelease.Release.FormatText != null)
        {
            description.Append($" - *{discogsRelease.Release.FormatText}*");
        }

        if (discogsRelease.Rating.HasValue)
        {
            description.Append($" - ");

            for (var i = 0; i < discogsRelease.Rating; i++)
            {
                description.Append("<:star:1043647352273121311>");
            }
        }

        description.AppendLine();

        return description.ToString();
    }

    public static string UserDiscogsReleaseToSimpleString(UserDiscogsReleases discogsRelease)
    {
        var description = new StringBuilder();

        if (discogsRelease.Release.FormatText != null)
        {
            description.Append($"{discogsRelease.Release.FormatText} ");
        }

        description.Append(discogsRelease.Release.Format);

        if (discogsRelease.Rating.HasValue)
        {
            description.Append($" ");

            for (var i = 0; i < discogsRelease.Rating; i++)
            {
                description.Append("⭐");
            }
        }

        return description.ToString();
    }

    public static string UserDiscogsWithAlbumName(UserDiscogsReleases discogsRelease)
    {
        var description = new StringBuilder();

        var formatEmote = GetDiscogsFormatEmote(discogsRelease.Release.Format);
        description.Append(formatEmote ?? discogsRelease.Release.Format);

        description.Append(
            $" - **[{discogsRelease.Release.Title}]({Constants.DiscogsReleaseUrl}{discogsRelease.Release.DiscogsId})**");

        if (discogsRelease.Release.FormatText != null)
        {
            description.Append($" - *{discogsRelease.Release.FormatText}*");
        }

        if (discogsRelease.Rating.HasValue)
        {
            description.Append($" - ");

            for (var i = 0; i < discogsRelease.Rating; i++)
            {
                description.Append("<:star:1043647352273121311>");
            }
        }

        description.AppendLine();

        return description.ToString();
    }

    public static string GetDiscogsFormatEmote(string format)
    {
        switch (format)
        {
            case "Vinyl":
                return "<:vinyl:1043644602969763861>";
            case "CD":
                return "💿";
            case "Cassette":
                return "<:casette:1043890774384853012>";
            case "File":
                return "📁";
            case "CDr":
                return "💿";
            case "DVD":
                return "📀";
            case "Box Set":
                return "📦";
            case "Flexi-disc":
                return "<:vinyl:1043644602969763861>";
            case "Blu-ray":
                return "📀";
            case "8-Track Cartridge":
                return "<:casette:1043890774384853012>";
        }

        return null;
    }

    public static string StringListToLongString(List<string> name)
    {
        var longString = new StringBuilder();
        for (var i = 0; i < name.Count; i++)
        {
            if (i != 0)
            {
                longString.Append(" - ");
            }

            var genre = name[i];
            longString.Append($"{genre}");
        }

        return longString.ToString();
    }
}
