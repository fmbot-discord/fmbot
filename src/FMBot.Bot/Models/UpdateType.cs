using System;

namespace FMBot.Bot.Models;

[Flags]
public enum UpdateType
{
    RecentPlays = 1 << 1,
    AllPlays = 1 << 2,
    Full = 1 << 3,
    Artist = 1 << 4,
    Albums = 1 << 5,
    Tracks = 1 << 6,
    Discogs = 1 << 7
}
