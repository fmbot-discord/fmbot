using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FMBot.Bot.Extensions;

public static class LongExtensions
{
    public static string ToFormattedByteString(this long bytes)
    {
        string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
    }
}
