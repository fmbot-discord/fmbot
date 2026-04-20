using System;

namespace FMBot.Persistence.Repositories;

internal static class UserLibrarySearch
{
    public static string[] BuildPatterns(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        var patterns = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            patterns[i] = "%" + Escape(tokens[i]) + "%";
        }

        return patterns;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
