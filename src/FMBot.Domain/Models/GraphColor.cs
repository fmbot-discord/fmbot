using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum GraphColor
{
    [Option("Image color", "Default - Color of the album cover or artist image, .fmbot blue when there is none")]
    ImageColor = 1,

    [Option(".fmbot blue", "The classic .fmbot graph color")]
    FmbotBlue = 2,

    [Option("Discord role color", "Uses the same color as your name")]
    RoleColor = 3,

    [Option("Custom", "Set your own color with a hex code")]
    Custom = 4
}
