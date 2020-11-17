using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FMBot.Bot.Extensions
{
    public static class StringExtensions
    {
        public static IEnumerable<string> SplitByMessageLength(this string str)
        {
            var messageLength = 2000;

            for (var index = 0; index < str.Length; index += messageLength)
            {
                yield return str.Substring(index, Math.Min(messageLength, str.Length - index));
            }
        }

        public static string FilterOutMentions(this string str)
        {
            var pattern = new Regex("(@everyone|@here|<@|`)");
            return pattern.Replace(str, "");
        }

        public static bool ContainsAny(this string haystack, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (haystack.Contains(needle))
                    return true;
            }

            return false;
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
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
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

        public static string GetPlaysString(long? playcount)
        {
            return playcount == 1 ? "play" : "plays";
        }

        public static string GetListenersString(long? listeners)
        {
            return listeners == 1 ? "listener" : "listeners";
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
    }
}
