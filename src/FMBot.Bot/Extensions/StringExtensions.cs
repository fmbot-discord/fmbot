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

            for (int index = 0; index < str.Length; index += messageLength)
            {
                yield return str.Substring(index, Math.Min(messageLength, str.Length - index));
            }
        }

        public static string FilterOutMentions(this string str)
        {
            var pattern = new Regex("(@everyone|@here|<@)");
            return pattern.Replace(str, "");
        }

        public static bool ContainsMentions(this string str)
        {
            var matchesPattern = Regex.Match(str, "(@everyone|@here|<@)");
            return matchesPattern.Success;
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
                return str;
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
    }
}
