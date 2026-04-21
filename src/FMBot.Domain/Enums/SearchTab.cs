using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum SearchTab
{
    [Option("Tracks")]
    Tracks = 0,
    [Option("Albums")]
    Albums = 1,
    [Option("Artists")]
    Artists = 2,
    [Option("Plays")]
    Plays = 3
}
