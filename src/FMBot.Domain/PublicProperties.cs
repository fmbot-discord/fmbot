using System.Collections.Concurrent;

namespace FMBot.Domain;

public static class PublicProperties
{
    public static bool IssuesAtLastFm = false;
    public static string IssuesReason = null;
    public static ConcurrentDictionary<string, ulong> SlashCommands = new();
}
