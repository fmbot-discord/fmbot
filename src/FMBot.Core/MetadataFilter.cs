using System;
using System.Text.RegularExpressions;

namespace FMBot.Core;

public static class MetadataFilter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly record struct FilterRule(Regex Source, string Target, bool Global);

    private static FilterRule Rule(string pattern, string target, RegexOptions options = RegexOptions.None, bool global = false)
        => new(new Regex(pattern, options | RegexOptions.Compiled, RegexTimeout), target, global);

    private static readonly FilterRule[] YouTubeTrackFilterRules =
    [
        Rule(@"^\s+|\s+$", "", global: true),
        Rule(@"\*+\s?\S+\s?\*+$", ""),
        Rule(@"\[[^\]]+\]", ""),
        Rule(@"【[^】]+】", ""),
        Rule(@"（[^）]+）", ""),
        Rule(@"\([^)]*version\)$", "", RegexOptions.IgnoreCase),
        Rule(@"\.(avi|wmv|mpg|mpeg|flv)$", "", RegexOptions.IgnoreCase),
        Rule(@"\(.*lyrics?\s*(video)?\)", "", RegexOptions.IgnoreCase),
        Rule(@"\((of+icial\s*)?(track\s*)?stream\)", "", RegexOptions.IgnoreCase),
        Rule(@"\((of+icial\s*)?((music|hd)\s*)?(video|audio)\)", "", RegexOptions.IgnoreCase),
        Rule(@"-\s(of+icial\s*)?(music\s*)?(video|audio)$", "", RegexOptions.IgnoreCase),
        Rule(@"\(.*Album\sTrack\)", "", RegexOptions.IgnoreCase),
        Rule(@"\(\s*of+icial\s*\)", "", RegexOptions.IgnoreCase),
        Rule(@"\(\s*[0-9]{4}\s*\)", "", RegexOptions.IgnoreCase),
        Rule(@"\(\s*(HD|HQ)\s*\)$", ""),
        Rule(@"(HD|HQ)\s?$", ""),
        Rule(@"(vid[ée]o)?\s?clip\sof+ici[ae]l", "", RegexOptions.IgnoreCase),
        Rule(@"of+iziel+es\s*video", "", RegexOptions.IgnoreCase),
        Rule(@"vid[ée]o\s?clip", "", RegexOptions.IgnoreCase),
        Rule(@"\sclip", "", RegexOptions.IgnoreCase),
        Rule(@"full\s*album", "", RegexOptions.IgnoreCase),
        Rule(@"\(live.*?\)$", "", RegexOptions.IgnoreCase),
        Rule(@"\|.*$", "", RegexOptions.IgnoreCase),
        Rule(@"^(|.*\s)""(.{5,})""(\s.*|)$", "$2"),
        Rule(@"^(|.*\s)'(.{5,})'(\s.*|)$", "$2"),
        Rule(@"\(.*[0-9]{1,2}\/[0-9]{1,2}\/[0-9]{2,4}.*\)", "", RegexOptions.IgnoreCase),
        Rule(@"sub\s*español", "", RegexOptions.IgnoreCase),
        Rule(@"\s\(Letra\)", "", RegexOptions.IgnoreCase),
        Rule(@"\s\(En\svivo\)", "", RegexOptions.IgnoreCase),
    ];

    private static readonly FilterRule[] TrimSymbolsFilterRules =
    [
        Rule(@"\(+\s*\)+", ""),
        Rule(@"^[/,:;~\s""-]+", ""),
        Rule(@"[/,:;~\s""-]+$", ""),
        Rule(@" {1,}", " "),
    ];

    private static readonly FilterRule[] YouTubeRules =
        [..YouTubeTrackFilterRules, ..TrimSymbolsFilterRules];

    public static string CleanTrackName(string trackName)
    {
        if (string.IsNullOrEmpty(trackName))
        {
            return trackName;
        }

        var result = trackName;
        foreach (var rule in YouTubeRules)
        {
            result = rule.Global
                ? rule.Source.Replace(result, rule.Target)
                : rule.Source.Replace(result, rule.Target, 1);
        }

        return result;
    }
}
