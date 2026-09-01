using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum GraphColor
{
    [Option("Same as embed", "Default - Matches the accent color of the embed the graph is in")]
    EmbedColor = 1,

    [Option(".fmbot blue", "The classic .fmbot graph color")]
    FmbotBlue = 2,

    [Option("Discord role color", "Uses the same color as your name")]
    RoleColor = 3,

    [Option("Custom", "Set your own color with a hex code")]
    Custom = 4
}
