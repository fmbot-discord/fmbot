using FMBot.Domain.Enums;

namespace FMBot.Bot.Models;

public class TopListSettings
{
    public TopListSettings()
    {
        EmbedSize = EmbedSize.Default;
    }

    public TopListSettings(EmbedSize embedSize)
    {
        this.EmbedSize = embedSize;
    }

    public TopListSettings(EmbedSize embedSize, bool billboard, bool discogs = false)
    {
        this.EmbedSize = embedSize;
        this.Billboard = billboard;
        this.Discogs = discogs;
    }

    public EmbedSize EmbedSize { get; set; }
    public bool Billboard { get; set; }
    public bool Discogs { get; set; }
    public string NewSearchValue { get; set; }

    public TopListType Type { get; set; }
}

public enum TopListType
{
    Plays,
    TimeListened
}
