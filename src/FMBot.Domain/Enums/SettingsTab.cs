using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum SettingsTab
{
    [Option("User settings")]
    User = 1,
    [Option("Server settings")]
    Server = 2,
    [Option("Premium server")]
    Premium = 3
}
