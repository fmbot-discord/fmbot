
using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models
{
    public enum GapEntityType
    {
        [Option("Artists")]
        Artist,
        [Option("Albums")]
        Album,
        [Option("Tracks")]
        Track
    }
}
