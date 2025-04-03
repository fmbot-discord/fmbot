using System;

namespace FMBot.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SupporterEnhancedAttribute(string explainer) : Attribute
{
    public string Explainer { get; private set; } = explainer;
}
