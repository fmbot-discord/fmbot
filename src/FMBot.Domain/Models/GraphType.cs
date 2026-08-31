using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum GraphType
{
    [Option("Line", "Line graph (default)")]
    Line = 1,

    [Option("Bar", "Bar chart, like on Last.fm")]
    Bar = 2,

    [Option("Off", "Don't show graphs on commands")]
    Off = 3
}
