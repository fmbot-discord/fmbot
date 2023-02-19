using System.Collections.Generic;

namespace FMBot.Domain;

public static class PublicProperties
{
    public static bool IssuesAtLastFm = false;
    public static string IssuesReason = null;
    public static Dictionary<string, ulong> SlashCommands = new();
}
