using System;
using System.Linq;
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

    [GeneratedRegex(@":[a-z0-9_+\-]+[:;]|@everyone|@here|://")]
    private static partial Regex LinkBreakingTextRegex();

    [GeneratedRegex("%25(?!2B)", RegexOptions.IgnoreCase)]
    private static partial Regex UnlinkablePercentRegex();

    public static string MarkdownLink(string text, string url)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || text != Sanitize(text) ||
            string.IsNullOrWhiteSpace(text.Replace("\u3164", "")) || text.IndexOfAny(['<', '\n']) >= 0 ||
            text.Contains('[') && (text.Length > 100 || text.Count(c => c == '[') != text.Count(c => c == ']')) ||
            ContainsEmoji(text) || LinkBreakingTextRegex().IsMatch(text))
        {
            return Sanitize(text);
        }

        var linkUrl = uri.AbsoluteUri.Replace("(", "%28").Replace(")", "%29").Replace("*", "%2A");

        return UnlinkablePercentRegex().IsMatch(linkUrl) ? text : $"[{text}]({linkUrl})";
    }

    public static bool ContainsEmoji(string text)
    {
        return !string.IsNullOrEmpty(text) && text.EnumerateRunes().Any(rune => rune.Value is
            >= 0x1F000 and <= 0x1FAFF or >= 0x2190 and <= 0x21FF or >= 0x2300 and <= 0x23FF or
            >= 0x25A0 and <= 0x27BF or >= 0x2934 and <= 0x2935 or >= 0x2B00 and <= 0x2BFF or
            0x203C or 0x2049 or 0x2139 or 0x24C2 or 0x3030 or 0x303D or 0x3297 or 0x3299 or
            0x200D or 0x20E3 or 0xFE0F);
    }

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
            description.AppendLine($"### {MarkdownLink(track.TrackName, track.TrackUrl)}");
        }
        else
        {
            description.AppendLine($"**{MarkdownLink(track.TrackName, track.TrackUrl)}**");
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
