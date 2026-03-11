using System.Collections.Generic;
using FMBot.Bot.Extensions;
using NetCord;

namespace FMBot.Bot.Models;

public class FaqData
{
    public List<FaqCategory> Categories { get; set; }
}

public class FaqCategory
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Emoji { get; set; }
    public string Description { get; set; }
    public List<FaqQuestion> Questions { get; set; }

    public EmojiProperties GetEmojiProperties()
    {
        var colonIndex = Emoji.IndexOf(':');
        if (colonIndex >= 0 && ulong.TryParse(Emoji[(colonIndex + 1)..], out var customId))
        {
            return EmojiProperties.Custom(customId);
        }

        return EmojiProperties.Standard(Emoji);
    }

    public string GetEmojiText()
    {
        var emojiProperties = GetEmojiProperties();
        var colonIndex = Emoji.IndexOf(':');
        var name = colonIndex >= 0 ? Emoji[..colonIndex] : Emoji;

        return emojiProperties.ToDiscordString(name);
    }
}

public class FaqQuestion
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Answer { get; set; }
}
