using System;

namespace FMBot.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SummaryAttribute(string summary) : Attribute
{
    public string Summary { get; private set; } = summary;
}
