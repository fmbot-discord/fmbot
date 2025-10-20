using System;

namespace FMBot.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ModuleNameAttribute(string moduleName) : Attribute
{
    public string ModuleName { get; private set; } = moduleName;
}
