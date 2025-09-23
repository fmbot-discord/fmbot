using FMBot.Domain.Enums;

namespace FMBot.Bot.Models;

public class TopListSettings
{
    public TopListSettings()
    {
        EmbedSize = EmbedSize.Default;
        ListAmount = 400;
    }

    public TopListSettings(EmbedSize embedSize)
    {
        this.EmbedSize = embedSize;
        ListAmount = 400;
        if (embedSize == EmbedSize.Large)
        {
            ListAmount = 1000;
        }
    }

    public TopListSettings(EmbedSize embedSize, bool billboard, bool discogs = false, int? year = null, int? decade = null)
    {
        this.EmbedSize = embedSize;
        this.Billboard = billboard;
        this.Discogs = discogs;
        this.ReleaseYearFilter = year;
        this.ReleaseDecadeFilter = decade;
        ListAmount = 400;
        if (embedSize == EmbedSize.Large)
        {
            ListAmount = 1000;
        }
    }

    public EmbedSize EmbedSize { get; set; }
    public int ListAmount { get; set; }
    public bool Billboard { get; set; }
    public bool Discogs { get; set; }
    public string NewSearchValue { get; set; }
    public int? ReleaseYearFilter { get; set; }
    public int? ReleaseDecadeFilter { get; set; }

    public TopListType Type { get; set; }
}

public enum TopListType
{
    Plays,
    TimeListened
}
