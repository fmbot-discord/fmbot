using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CommandCategoriesAttribute : Attribute
{
    public CommandCategory[] Categories { get; }

    public CommandCategoriesAttribute(params CommandCategory[] categories)
    {
        this.Categories = categories;
    }
}
