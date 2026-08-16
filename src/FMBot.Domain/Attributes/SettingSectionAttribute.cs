using System;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Attributes;

public class SettingSectionAttribute : Attribute
{
    public GuildSettingSection Section { get; private set; }

    public SettingSectionAttribute(GuildSettingSection section)
    {
        this.Section = section;
    }
}
