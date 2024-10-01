using System;

namespace FMBot.Bot.Attributes;

public class EmbedOptionAttribute : Attribute
{
    public string ScriptName { get; private set; }

    public EmbedOptionAttribute(string scriptName)
    {
        this.ScriptName = scriptName;
    }
}
