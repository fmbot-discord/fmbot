using System;
using System.Linq;

namespace FMBot.Domain.Attributes;

public class OptionAttribute : Attribute
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public bool SupporterOnly { get; private set; }

    public OptionAttribute(string name, string description, bool supporterOnly = false)
    {
        this.Name = name;
        this.Description = description;
        this.SupporterOnly = supporterOnly;
    }
}
