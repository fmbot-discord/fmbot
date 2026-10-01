using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using FMBot.Domain.Models;

namespace FMBot.Domain.Extensions;

public static partial class DiscordStringExtensions
{
    private static readonly string[] SensitiveCharacters =
    {
        "\\",
        "*",
        "_",
        "`",
        ">",
    };

    [GeneratedRegex(@"^(#{1,3}|-#)(?= )", RegexOptions.Multiline)]
    private static partial Regex LineStartMarkdownRegex();

    public static string Sanitize(string text)
    {
        if (text != null)
        {
            foreach (string sensitiveCharacter in SensitiveCharacters)
            {
                text = text.Replace(sensitiveCharacter, "\\" + sensitiveCharacter);
            }

            text = text.Replace("~~", "\\~\\~");
            text = text.Replace("||", "\\|\\|");
            text = LineStartMarkdownRegex().Replace(text, @"\$1");
        }

        return text;
    }

    public static string GetRymUrl(string albumName, string artistName)
    {
        var albumRymUrl = new StringBuilder();
        albumRymUrl.Append(@"https://rateyourmusic.com/search?searchterm=");
        albumRymUrl.Append(
            HttpUtility.UrlEncode($"{artistName} {albumName.Replace("- Single", "").Replace("- EP", "").TrimEnd()}"));
        albumRymUrl.Append($"&searchtype=l");

        return albumRymUrl.ToString();
    }

    public static string TrackToLinkedString(RecentTrack track, bool? rymEnabled = null, bool bigTrackName = true)
    {
        var description = new StringBuilder();

        if (bigTrackName)
        {
            description.AppendLine($"### [{Sanitize(track.TrackName)}]({track.TrackUrl})");
        }
        else
        {
            description.AppendLine($"**[{Sanitize(track.TrackName)}]({track.TrackUrl})**");
        }

        description.Append($"**{Sanitize(track.ArtistName)}**");

        if (!string.IsNullOrWhiteSpace(track.AlbumName))
        {
            if (rymEnabled == true)
            {
                var albumRymUrl = GetRymUrl(track.AlbumName, track.ArtistName);

                description.Append($" • *[{Sanitize(track.AlbumName)}]({albumRymUrl})*");
            }
            else
            {
                description.Append($" • *{Sanitize(track.AlbumName)}*");
            }
        }

        description.AppendLine();
        return description.ToString();
    }
}
