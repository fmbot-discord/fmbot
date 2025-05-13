using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FMBot.Bot.Extensions;

public static class LongExtensions
{
    public static string ToFormattedByteString(this long bytes)
    {
        string[] suffix = ["B", "KB", "MB", "GB", "TB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return $"{dblSByte:0.##} {suffix[i]}";
    }
}
