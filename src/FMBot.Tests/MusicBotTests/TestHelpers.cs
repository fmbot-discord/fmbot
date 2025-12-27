using System.Text.RegularExpressions;

namespace FMBot.Tests.MusicBotTests;

/// <summary>
/// Helper class for testing music bot parsing logic without requiring NetCord Message objects.
/// These methods mirror the parsing logic in the actual MusicBot implementations.
/// </summary>
public static partial class MusicBotTestHelper
{
    /// <summary>
    /// Tests JockieMusicBot parsing: extracts "song by artist" from description
    /// </summary>
    public static string ParseJockieFormat(string description)
    {
        const string startedPlaying = " ​ Started playing ";
        if (description.Contains(startedPlaying, StringComparison.OrdinalIgnoreCase))
        {
            var songByArtist = description[description.IndexOf(startedPlaying, StringComparison.OrdinalIgnoreCase)..];
            return songByArtist.Replace("\\", "");
        }
        return string.Empty;
    }

    /// <summary>
    /// Tests SoundCloudMusicBot parsing: returns the description directly
    /// </summary>
    public static string ParseSoundCloudFormat(string description) => description;

    /// <summary>
    /// Tests MakiMusicBot parsing: removes ** from description
    /// </summary>
    public static string ParseMakiFormat(string description) => description.Replace("**", "");

    /// <summary>
    /// Tests BleedMusicBot parsing: extracts track and artist from "Now playing [`track`](url) by **artist**" format
    /// </summary>
    public static string ParseBleedFormat(string description)
    {
        const string startedPlaying = "Now playing ";
        var embedTrack = description.Replace(startedPlaying, "", StringComparison.OrdinalIgnoreCase);
        const string replacement = "$1 - $2";
        return BleedRegex().Replace(embedTrack, replacement);
    }

    [GeneratedRegex(@"\[\`(.+?)\`\]\(.+?\) by \*\*(.+?)\*\*")]
    private static partial Regex BleedRegex();

    /// <summary>
    /// Tests GreenBotMusicBot parsing: extracts from "[track](url) by [artist](url)" format
    /// </summary>
    public static string ParseGreenBotFormat(string description)
    {
        var matches = Regex.Matches(description, @"\[(.*?)\]\(.*?\)");

        if (matches.Count < 2)
        {
            return string.Empty;
        }

        var songName = matches[0].Groups[1].Value.Trim();
        var artist = matches[1].Groups[1].Value.Trim();

        return $"{artist} - {songName}";
    }

    /// <summary>
    /// Tests FlaviMusicBot parsing - note: FlaviMusicBot now uses Components, not embeds.
    /// This helper just removes ### prefix for basic string testing.
    /// </summary>
    public static string ParseFlaviFormat(string description) => description.Replace("### ", "").Replace("**", "");

    /// <summary>
    /// Tests CakeyBotMusicBot parsing: extracts from "Now playing: [Artist - Track](url)" format
    /// </summary>
    public static string ParseCakeyFormat(string description)
    {
        const string nowPlaying = "Now playing: ";
        var pFrom = description.IndexOf(nowPlaying, StringComparison.Ordinal) + nowPlaying.Length;
        var pTo = description.LastIndexOf(" [", StringComparison.Ordinal);

        if (pFrom < nowPlaying.Length || pTo < 0 || pTo <= pFrom)
        {
            return description;
        }

        return description.Substring(pFrom, pTo - pFrom);
    }

    /// <summary>
    /// Tests ListenMusicBot parsing: combines title and description (with "-# " prefix removal)
    /// </summary>
    public static string ParseListenFormat(string title, string description)
    {
        var artist = description.Replace("-# ", "").Trim();
        return $"{artist} - {title}";
    }

    /// <summary>
    /// Tests BettyMusicBot parsing: extracts from "artist | track" format in MediaGallery item descriptions
    /// </summary>
    public static string ParseBettyFormat(string description)
    {
        var parts = description.Split('|');
        if (parts.Length == 2)
        {
            var artist = parts[0].Trim();
            var track = parts[1].Trim();
            return $"{artist} - {track}";
        }
        return description;
    }
}
