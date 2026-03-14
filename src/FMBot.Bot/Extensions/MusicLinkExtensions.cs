using System;
using System.Text.RegularExpressions;

namespace FMBot.Bot.Extensions;

public static partial class MusicLinkExtensions
{
    public enum MusicLinkType
    {
        SpotifyTrack,
        SpotifyAlbum,
        SpotifyArtist,
        AppleMusicSong,
        AppleMusicAlbum,
        AppleMusicArtist
    }

    public record MusicLinkResult(MusicLinkType Type, string Id);

    [GeneratedRegex(@"(?:https:\/\/open\.spotify\.com\/(?:intl-[a-z]{2}\/)?(track|album|artist)\/|spotify:(track|album|artist):)([a-zA-Z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyLinkRegex();

    [GeneratedRegex(@"https:\/\/music\.apple\.com\/[a-z]{2}\/(song|album|artist)\/[^\/\s]+\/(\d+)(?:\?i=(\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex AppleMusicLinkRegex();

    public static MusicLinkResult TryParseMusicLink(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (!input.Contains("spotify", StringComparison.OrdinalIgnoreCase) &&
            !input.Contains("apple.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var spotifyMatch = SpotifyLinkRegex().Match(input);
        if (spotifyMatch.Success)
        {
            var type = (spotifyMatch.Groups[1].Success ? spotifyMatch.Groups[1].Value : spotifyMatch.Groups[2].Value).ToLower();
            var id = spotifyMatch.Groups[3].Value;

            var linkType = type switch
            {
                "track" => MusicLinkType.SpotifyTrack,
                "album" => MusicLinkType.SpotifyAlbum,
                "artist" => MusicLinkType.SpotifyArtist,
                _ => (MusicLinkType?)null
            };

            if (linkType.HasValue)
            {
                return new MusicLinkResult(linkType.Value, id);
            }
        }

        var appleMusicMatch = AppleMusicLinkRegex().Match(input);
        if (appleMusicMatch.Success)
        {
            var type = appleMusicMatch.Groups[1].Value.ToLower();
            var primaryId = appleMusicMatch.Groups[2].Value;
            var songIdParam = appleMusicMatch.Groups[3].Success ? appleMusicMatch.Groups[3].Value : null;

            return type switch
            {
                "song" => new MusicLinkResult(MusicLinkType.AppleMusicSong, primaryId),
                "album" when songIdParam != null => new MusicLinkResult(MusicLinkType.AppleMusicSong, songIdParam),
                "album" => new MusicLinkResult(MusicLinkType.AppleMusicAlbum, primaryId),
                "artist" => new MusicLinkResult(MusicLinkType.AppleMusicArtist, primaryId),
                _ => null
            };
        }

        return null;
    }
}
