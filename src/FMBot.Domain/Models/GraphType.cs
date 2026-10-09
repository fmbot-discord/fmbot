using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum GraphType
{
    [Option("Line", "Line graph")]
    Line = 1,

    [Option("Bar", "Bar chart (default)")]
    Bar = 2,

    [Option("Off", "Don't show graphs on commands")]
    Off = 3
}
