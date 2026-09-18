using System;
using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Models;

namespace FMBot.Core;

public static class TasteCalculator
{
    public static List<TasteMatch> GetMatches(IEnumerable<TasteItem> own, IEnumerable<TasteItem> other,
        StringComparer comparer)
    {
        var otherLookup = other.ToLookup(l => l.Name, comparer);

        return own
            .Where(w => otherLookup.Contains(w.Name))
            .OrderByDescending(o => o.Playcount)
            .Select(s => new TasteMatch(s.Name, s.Playcount, otherLookup[s.Name].First().Playcount))
            .ToList();
    }

    public static decimal MatchPercentage(int ownCount, int matchCount)
    {
        if (ownCount == 0 || matchCount == 0)
        {
            return 0;
        }

        return (decimal)matchCount / ownCount * 100;
    }

    public static List<TasteMatch> SelectRows(IReadOnlyCollection<TasteMatch> matches, int amount)
    {
        var filterAmount = 0;
        for (var i = 0; i < 100; i++)
        {
            if (matches.Count(w => w.OwnPlaycount >= i && w.OtherPlaycount >= i) <= amount)
            {
                filterAmount = i;
                break;
            }
        }

        return matches
            .Where(w => w.OwnPlaycount >= filterAmount && w.OtherPlaycount >= filterAmount)
            .Take(amount)
            .ToList();
    }
}
