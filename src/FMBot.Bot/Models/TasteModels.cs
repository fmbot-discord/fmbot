using System.Collections.Generic;
using FMBot.Domain.Enums;
using NetCord;

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

public class TasteCacheModel
{
    public List<TastePageData> Pages { get; set; } = [];
    public Color AccentColor { get; set; }
    public int Amount { get; set; }

    public TasteRawData RawData { get; set; }
}

public class TastePageData
{
    public string Label { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string Url { get; set; }
}

public class TasteRawData
{
    public string OwnUsername { get; set; }
    public string OtherUsername { get; set; }
    public string OwnName { get; set; }
    public string OtherName { get; set; }
    public string Url { get; set; }
    public string TimeDescription { get; set; }

    public List<TasteItem> OwnTopArtists { get; set; }
    public List<TasteItem> OtherTopArtists { get; set; }
    public List<TasteItem> OwnTopGenres { get; set; }
    public List<TasteItem> OtherTopGenres { get; set; }
    public List<TasteItem> OwnTopCountries { get; set; }
    public List<TasteItem> OtherTopCountries { get; set; }
    public List<TasteItem> OwnDiscogsArtists { get; set; }
    public List<TasteItem> OtherDiscogsArtists { get; set; }
    public string DiscogsOwnUsername { get; set; }
    public string DiscogsOtherUsername { get; set; }
    public string DiscogsUrl { get; set; }
}
