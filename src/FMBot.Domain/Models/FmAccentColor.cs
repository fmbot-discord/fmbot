using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum FmAccentColor
{
    [Option("Last.fm red", "Classic Last.fm red color")]
    LastFmRed = 1,
    [Option("Average album cover color", "Default - Average color first, Apple Music background after")]
    CoverColor = 2,
    [Option("Apple Music background color", "Apple Music background color first, average color after")]
    AppleMusicBackgroundColor = 3,
    [Option("⭐ Discord role color", "Uses the same color as your name", supporterOnly: true)]
    RoleColor = 4,
    [Option("⭐ Custom", "Set your own color with a hex code", supporterOnly: true)]
    Custom = 5
}
