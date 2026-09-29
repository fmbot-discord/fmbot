using System.Collections.Generic;
using System.Linq;

namespace FMBot.Bot.Extensions;

public static class ListExtensions
{
    public static List<List<T>> ChunkBy<T>(this IReadOnlyList<T> source, int chunkSize)
    {
        return source
            .Select((x, i) => new { Index = i, Value = x })
            .GroupBy(x => x.Index / chunkSize)
            .Select(x => x.Select(v => v.Value).ToList())
            .ToList();
    }

    public static int IndexOf<T>(this IReadOnlyList<T> source, T item)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(source[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
