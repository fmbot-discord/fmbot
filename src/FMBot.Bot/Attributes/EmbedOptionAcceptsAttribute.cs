using System;
using FMBot.Bot.Models.TemplateOptions;

namespace FMBot.Bot.Attributes;

public class EmbedOptionAcceptsAttribute : Attribute
{
    public VariableType[] VariableTypes { get; }

    public EmbedOptionAcceptsAttribute(params VariableType[] variableTypes)
    {
        this.VariableTypes = variableTypes;
    }
}

