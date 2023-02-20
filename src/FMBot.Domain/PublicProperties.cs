using System.Collections.Concurrent;

namespace FMBot.Domain;

public static class PublicProperties
{
    public static bool IssuesAtLastFm = false;
    public static string IssuesReason = null;

    public static readonly ConcurrentDictionary<string, ulong> SlashCommands = new();
    public static readonly ConcurrentDictionary<ulong, int> PremiumServers = new();
    public static readonly ConcurrentDictionary<ulong, int> RegisteredUsers = new();
}
