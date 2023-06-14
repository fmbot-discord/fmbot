using System.Collections.Generic;
using System;
using System.Linq;
using FMBot.Domain.Models;

namespace FMBot.Bot.Extensions;

public static class FlagExtensions
{
    public static IEnumerable<T> GetUniqueFlags<T>(this T flags)
        where T : Enum
    {
        return from Enum value in Enum.GetValues(flags.GetType()) where flags.HasFlag(value) select (T)value;
    }
}
