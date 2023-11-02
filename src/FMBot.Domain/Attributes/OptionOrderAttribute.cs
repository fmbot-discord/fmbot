using System;

namespace FMBot.Domain.Attributes;

public class OptionOrderAttribute : Attribute
{

    public int Order { get; private set; }

    public OptionOrderAttribute(int order)
    {
        this.Order = order;
    }
}
