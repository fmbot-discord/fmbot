using System;

namespace FMBot.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ExamplesAttribute : Attribute
{
    public string[] Examples { get; }

    public ExamplesAttribute(params string[] examples)
    {
        this.Examples = examples;
    }
}
