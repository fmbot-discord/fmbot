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
    }
}
