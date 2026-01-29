using System;

namespace FMBot.Domain.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class FmButtonAttribute : Attribute
{
    public ulong? CustomEmojiId { get; private set; }
    public string StandardEmoji { get; private set; }
    public string CustomId { get; private set; }
    public bool RequiresDbTrack { get; private set; }

    // Constructor for custom Discord emoji (link buttons - no customId)
    public FmButtonAttribute(ulong customEmojiId, bool requiresDbTrack = false)
    {
        CustomEmojiId = customEmojiId;
        RequiresDbTrack = requiresDbTrack;
    }

    // Constructor for custom Discord emoji (interaction buttons - with customId)
    public FmButtonAttribute(ulong customEmojiId, string customId, bool requiresDbTrack = false)
    {
        CustomEmojiId = customEmojiId;
        CustomId = customId;
        RequiresDbTrack = requiresDbTrack;
    }

    // Constructor for standard Unicode emoji (interaction buttons - with customId)
    public FmButtonAttribute(string standardEmoji, string customId, bool requiresDbTrack = false)
    {
        StandardEmoji = standardEmoji;
        CustomId = customId;
        RequiresDbTrack = requiresDbTrack;
    }
}
