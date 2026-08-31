using System.Collections.Frozen;
using System.Collections.Generic;

namespace FMBot.Bot.Resources;

public static class SlashCommandCanonicalNames
{
    private static readonly FrozenDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["wk"] = "whoknows",
        ["gwk"] = "globalwhoknows",
        ["fwk"] = "friendwhoknows",
        ["faq"] = "frequentlyasked"
    }.ToFrozenDictionary();

    public static string Resolve(string commandName)
    {
        return commandName != null && Names.TryGetValue(commandName, out var canonicalName)
            ? canonicalName
            : commandName;
    }
}
