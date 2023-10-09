using Discord;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class TasteSettings
{
    public TasteType TasteType { get; set; }

    public EmbedSize EmbedSize { get; set; } = EmbedSize.Default;
}

public class TasteModels
{
    public string Description { get; set; }

    public string LeftDescription { get; set; }

    public string RightDescription { get; set; }
}

public class TasteTwoUserModel
{
    public string Artist { get; set; }

    public long OwnPlaycount { get; set; }

    public long OtherPlaycount { get; set; }
}

public class TasteItem
{
    public TasteItem(string name, long playcount)
    {
        this.Name = name;
        this.Playcount = playcount;
    }

    public string Name { get; set; }

    public long Playcount { get; set; }
}

public enum TasteType
{
    FullEmbed = 1,
    Table = 2
}

public sealed record Item(string Name, IEmote Emote);
