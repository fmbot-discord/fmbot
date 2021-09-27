using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services
{
    public static class StringService
    {
        public static string TrackToLinkedString(RecentTrack track, bool? rymEnabled = null)
        {
            var escapedTrackName = Regex.Replace(track.TrackName, @"([|\\*])", @"\$1");

            if (!string.IsNullOrWhiteSpace(track.AlbumName))
            {
                if (rymEnabled == true)
                {
                    var albumQueryName = track.AlbumName.Replace(" - Single", "");
                    albumQueryName = albumQueryName.Replace(" - EP", "");

                    var escapedAlbumName = Regex.Replace(track.AlbumName, @"([|\\*])", @"\$1");
                    var albumRymUrl = @"https://duckduckgo.com/?q=%5Csite%3Arateyourmusic.com";
                    albumRymUrl += HttpUtility.UrlEncode($" \"{albumQueryName}\" \"{track.ArtistName}\"");

                    return $"[{escapedTrackName}]({track.TrackUrl})\n" +
                           $"By **{track.ArtistName}**" +
                           $" | *[{escapedAlbumName}]({albumRymUrl})*\n";
                }

                return $"[{escapedTrackName}]({track.TrackUrl})\n" +
                       $"By **{track.ArtistName}**" +
                       $" | *{track.AlbumName}*\n";
            }

            return $"[{escapedTrackName}]({track.TrackUrl})\n" +
                   $"By **{track.ArtistName}**\n";
        }

        public static string TrackToString(RecentTrack track)
        {
            return $"{track.TrackName}\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static StaticPaginator BuildStaticPaginator(IList<PageBuilder> pages, bool paginationEnabled = true)
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
    }
}
