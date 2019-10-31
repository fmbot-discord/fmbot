using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FMBot.Bot.Extensions
{
    public static class StringExtensions
    {
        public static IEnumerable<string> SplitByMessageLength(this string str)
        {
            int MessageLength = 2000;

            for (int index = 0; index < str.Length; index += MessageLength)
            {
                yield return str.Substring(index, Math.Min(MessageLength, str.Length - index));
            }
        }

        public static string FilterOutMentions(this string str)
        {
            var pattern = new Regex("(@everyone|@here|<@)");
            var cleanedPattern = pattern.Replace(str, "");
            return cleanedPattern;
        }
    }
}
