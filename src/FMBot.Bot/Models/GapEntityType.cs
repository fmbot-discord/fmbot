using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models
{
    public enum GapEntityType
    {
        [Option("Artist gaps")]
        Artist,
        [Option("Album gaps")]
        Album,
        [Option("Track gaps")]
        Track
    }
}
