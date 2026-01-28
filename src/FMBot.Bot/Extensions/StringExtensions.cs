using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;

namespace FMBot.Bot.Extensions;

public static partial class StringExtensions
{
    extension(string str)
    {
        public string FilterOutMentions()
        {
            var pattern = new Regex("(@everyone|@here|<@|`|http://|https://)");
            return pattern.Replace(str, "");
        }

        public bool ContainsEmoji()
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            return str.EnumerateRunes()
                .Select(rune => rune.Value)
                .Any(value => value is >= 0x2600 and <= 0x26FF || // Miscellaneous Symbols
                              value is >= 0x2700 and <= 0x27BF || // Dingbats
                              value is >= 0x2B00 and <= 0x2BFF || // Miscellaneous Symbols and Arrows
                              value is >= 0x1F300 and <= 0x1F5FF || // Miscellaneous Symbols and Pictographs
                              value is >= 0x1F600 and <= 0x1F64F || // Emoticons
                              value is >= 0x1F680 and <= 0x1F6FF || // Transport and Map Symbols
                              value is >= 0x1F1E6 and <= 0x1F1FF || // Regional Indicator Symbols (for flags)
                              value is >= 0x1F900 and <= 0x1F9FF || // Supplemental Symbols and Pictographs
                              value is >= 0x1FA70 and <= 0x1FAFF || // Symbols and Pictographs Extended-A
                              value is >= 0x1FB00 and <= 0x1FBFF || // Symbols for Legacy Computing
                              value == 0x200D || // Zero Width Joiner
                              value is >= 0xFE00 and <= 0xFE0F); // Variation Selectors
        }
    }

    public static bool ContainsUnicodeCharacter(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        const int MaxAnsiCode = 255;

        return input.Any(c => c > MaxAnsiCode);
    }

    public static string ReplaceInvalidChars(string filename)
    {
        var extension = Path.GetExtension(filename);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            filename = filename.Replace(extension, "");
        }

        filename = filename.Replace("\"", "_");
        filename = filename.Replace("'", "_");
        filename = filename.Replace(".", "_");
        filename = filename.Replace(" ", "_");

        var invalidChars = Path.GetInvalidFileNameChars();
        var result = string.Join("_", filename.Split(invalidChars));

        result = TruncateLongString(result, 80);

        if (!string.IsNullOrWhiteSpace(extension))
        {
            result += extension;
        }

        return result;
    }

    public static string SanitizeTrackNameForComparison(string trackName)
    {
        trackName = trackName.ToLower();
        trackName = trackName.Replace("(", "");
        trackName = trackName.Replace(")", "");
        trackName = trackName.Replace("-", "");
        trackName = trackName.Replace("'", "");
        trackName = trackName.Replace(" ", "");
        trackName = trackName.Replace("[", "");
        trackName = trackName.Replace("]", "");

        return trackName;
    }

    public static long ToUnixEpochDate(this DateTime @this)
    {
        DateTimeOffset dateTimeOffset = new(@this);
        dateTimeOffset = dateTimeOffset.ToUniversalTime();
        return dateTimeOffset.ToUnixTimeSeconds();
    }

    private static readonly string[] SensitiveCharacters =
    {
        "\\",
        "*",
        "_",
        "~",
        "`",
        ":",
        "/",
        ">",
        "|",
        "#",
    };

    public static string Sanitize(string text)
    {
        if (text != null)
        {
            foreach (string sensitiveCharacter in SensitiveCharacters)
            {
                text = text.Replace(sensitiveCharacter, "\\" + sensitiveCharacter);
            }
        }

        return text;
    }

    public static string UserTypeToIcon(this UserType userType)
    {
        switch (userType)
        {
            case UserType.Owner:
                return " 👑";
            case UserType.Admin:
                return " 🛡";
            case UserType.Contributor:
                return " 🔥";
            case UserType.Supporter:
                return " ⭐";
            default:
                return "";
        }
    }

    public static string TruncateLongString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        return str.Substring(0, Math.Min(str.Length, maxLength));
    }

    public static string KeyIntToPitchString(int key)
    {
        return key switch
        {
            0 => "C",
            1 => "C#",
            2 => "D",
            3 => "D#",
            4 => "E",
            5 => "F",
            6 => "F#",
            7 => "G",
            8 => "G#",
            9 => "A",
            10 => "A#",
            11 => "B",
            _ => null
        };
    }

    public static string CommandCategoryToString(CommandCategory commandCategory)
    {
        return commandCategory switch
        {
            CommandCategory.Artists => "Info, WhoKnows, tracks, albums, serverartists, topartists",
            CommandCategory.Tracks => "Info, WhoKnows, love, unlove, servertracks, toptracks",
            CommandCategory.Albums => "Info, WhoKnows, cover, serveralbums, topalbums",
            CommandCategory.WhoKnows => "Global, server, friends, settings",
            CommandCategory.Games => "Jumble and Pixel Jumble",
            CommandCategory.Friends => "Add, remove, WhoKnows, view",
            CommandCategory.Genres => "Info, WhoKnows, topgenres",
            CommandCategory.Charts => "Image charts",
            CommandCategory.Crowns => "Crowns commands and crown management",
            CommandCategory.ThirdParty => "Spotify, Discogs, Youtube, Apple Music and Genius",
            CommandCategory.UserSettings => "Configure your user settings",
            CommandCategory.ServerSettings => "Configure your server settings",
            CommandCategory.Importing => "Importing Spotify or Apple Music history",
            CommandCategory.Other => "Other",
            _ => null
        };
    }

    public static string GetPlaysString(long? playcount)
    {
        return playcount == 1 ? "play" : "plays";
    }

    public static string GetScrobblesString(long? scrobbles)
    {
        return scrobbles == 1 ? "scrobble" : "scrobbles";
    }

    public static string GetArtistsString(long? artists)
    {
        return artists == 1 ? "artist" : "artists";
    }

    public static string GetAlbumsString(long? albums)
    {
        return albums == 1 ? "album" : "albums";
    }

    public static string GetListenersString(long? listeners)
    {
        return listeners == 1 ? "listener" : "listeners";
    }

    public static string GetFriendsString(long? friends)
    {
        return friends == 1 ? "friend" : "friends";
    }

    public static string GetCrownsString(long? crowns)
    {
        return crowns == 1 ? "crown" : "crowns";
    }

    public static string GetDaysString(long? days)
    {
        return days == 1 ? "day" : "days";
    }

    public static string GetReleasesString(long? releases)
    {
        return releases == 1 ? "release" : "releases";
    }

    public static string GetUsersString(long? users)
    {
        return users == 1 ? "user" : "users";
    }

    public static string GetRolesString(long? roles)
    {
        return roles == 1 ? "role" : "roles";
    }

    public static string GetItemsString(long? items)
    {
        return items == 1 ? "item" : "items";
    }

    public static string GetTimesString(long? times)
    {
        return times == 1 ? "time" : "times";
    }

    public static string GetHintsString(long? hints)
    {
        return hints == 1 ? "hint" : "hints";
    }

    public static string GetGamesString(long? games)
    {
        return games == 1 ? "game" : "games";
    }

    public static string GetChangeString(decimal oldValue, decimal newValue)
    {
        if (oldValue < newValue)
        {
            return "Up from";
        }

        return oldValue > newValue ? "Down from" : "From";
    }

    public static string GetTrackLength(long trackLength)
    {
        return GetTrackLength(TimeSpan.FromMilliseconds(trackLength));
    }

    public static string GetTrackLength(TimeSpan trackLength)
    {
        return
            $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";
    }

    public static string GetListeningTimeString(TimeSpan timeSpan, bool includeSeconds = false)
    {
        var time = new StringBuilder();

        if (timeSpan.TotalDays >= 1)
        {
            time.Append($"{(int)timeSpan.TotalDays}");
            time.Append("d");
        }

        if (timeSpan.TotalHours >= 1)
        {
            time.Append($"{timeSpan.Hours}");
            time.Append("h");
        }

        time.Append($"{timeSpan.Minutes}");
        time.Append($"m");

        if (includeSeconds)
        {
            time.Append($"{timeSpan.Seconds}");
            time.Append("s");
        }

        return time.ToString();
    }

    public static string GetLongListeningTimeString(TimeSpan timeSpan)
    {
        var time = new StringBuilder();

        if (timeSpan.Days >= 2)
        {
            time.Append($"{(int)timeSpan.TotalDays} days");
            if (timeSpan.Hours > 0)
            {
                time.Append($", {timeSpan.Hours} ");
                time.Append(timeSpan.Hours == 1 ? $"hour" : $"hours");
            }

            return time.ToString();
        }

        if (timeSpan.Days == 1)
        {
            time.Append($"{(int)timeSpan.TotalDays} day");
            time.Append($", {timeSpan.Hours} ");
            time.Append(timeSpan.Hours == 1 ? $"hour" : $"hours");

            return time.ToString();
        }

        if (timeSpan.Hours >= 2)
        {
            time.Append($"{timeSpan.Hours} hours");
            if (timeSpan.Minutes > 0)
            {
                time.Append($", {timeSpan.Minutes} minutes");
            }

            return time.ToString();
        }

        if (timeSpan.Hours == 1)
        {
            time.Append($"{timeSpan.Hours} hour");
            if (timeSpan.Minutes > 0)
            {
                time.Append($", {timeSpan.Minutes} minutes");
            }

            return time.ToString();
        }

        time.Append($"{timeSpan.Minutes} minutes");

        return time.ToString();
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

    public static string GetAmountEnd(long amount)
    {
        var amountString = amount.ToString();

        return amountString switch
        {
            { } a when a.EndsWith("11") => "th",
            { } a when a.EndsWith("12") => "th",
            { } a when a.EndsWith("13") => "th",
            { } a when a.EndsWith("1") => "st",
            { } a when a.EndsWith("2") => "nd",
            { } a when a.EndsWith("3") => "rd",
            _ => "th"
        };
    }

    public static string GetTimeAgo(DateTime timeAgo)
    {
        var ts = new TimeSpan(DateTime.UtcNow.Ticks - timeAgo.Ticks);
        var delta = Math.Abs(ts.TotalSeconds);

        if (delta < 60)
        {
            return ts.Seconds == 1 ? "one second ago" : ts.Seconds + " seconds ago";
        }

        if (delta < 60 * 2)
        {
            return "a minute ago";
        }

        if (delta < 45 * 60)
        {
            return ts.Minutes + " minutes ago";
        }

        if (delta < 90 * 60)
        {
            return "an hour ago";
        }

        if (delta < 24 * 60 * 60)
        {
            return ts.Hours + " hours ago";
        }

        if (delta < 48 * 60 * 60)
        {
            return "yesterday";
        }

        if (delta < 30 * 24 * 60 * 60)
        {
            return ts.Days + " days ago";
        }

        if (delta < 12 * 30 * 24 * 60 * 60)
        {
            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
            return months <= 1 ? "one month ago" : months + " months ago";
        }

        return "more then a month ago";
    }

    public static string GetTimeAgoShortString(DateTime timeAgo)
    {
        var ts = new TimeSpan(DateTime.UtcNow.Ticks - timeAgo.Ticks);
        var delta = Math.Abs(ts.TotalSeconds);

        if (delta < 60)
        {
            return ts.Seconds + "s";
        }

        if (delta < 60 * 2)
        {
            return "1m";
        }

        if (delta < 45 * 60)
        {
            return ts.Minutes + "m";
        }

        if (delta < 90 * 60)
        {
            return "1h";
        }

        if (delta < 24 * 60 * 60)
        {
            return ts.Hours + "h";
        }

        if (delta < 48 * 60 * 60)
        {
            return "yesterday";
        }

        if (delta < 30 * 24 * 60 * 60)
        {
            return ts.Days + "d";
        }

        if (delta < 12 * 30 * 24 * 60 * 60)
        {
            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
            return months + " mnths";
        }

        var years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
        return years + "y";
    }

    public static void ReplaceOrAddToList(this List<string> currentList, IEnumerable<string> optionsToAdd)
    {
        foreach (var optionToAdd in optionsToAdd)
        {
            var existingOption = currentList.FirstOrDefault(f => f.ToLower() == optionToAdd.ToLower());

            if (existingOption != null && existingOption != optionToAdd)
            {
                var index = currentList.IndexOf(existingOption);
                if (index != -1)
                {
                    currentList[index] = optionToAdd;
                }
            }
            else if (existingOption == null)
            {
                currentList.Add(optionToAdd);
            }
        }
    }

    public static void ReplaceOrAddToDictionary(this Dictionary<string, string> currentDictionary,
        Dictionary<string, string> optionsToAdd)
    {
        foreach (var optionToAdd in optionsToAdd)
        {
            if (!currentDictionary.TryGetValue(optionToAdd.Key, out _))
            {
                currentDictionary.Add(optionToAdd.Key, optionToAdd.Value);
            }
        }
    }

    public static string RemoveEditionSuffix(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        input = EditionRegex().Replace(input, "").Trim();
        input = KpopRegex().Replace(input, "");

        return input.Trim();
    }

    [GeneratedRegex(@"\s*\([^)]*\)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex EditionRegex();

    [GeneratedRegex(@"\s*-\s*(The \d+(st|nd|rd|th) (Mini )?Album( Repackage)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex KpopRegex();

    public static string ToDiscordString(this EmojiProperties emoji, string name, bool animated = false)
    {
        if (!emoji.Id.HasValue)
        {
            return emoji.Name ?? string.Empty;
        }

        var prefix = animated ? "a" : "";
        return $"<{prefix}:{name}:{emoji.Id.Value}>";
    }
}
