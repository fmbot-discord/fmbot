namespace FMBot.Bot.Models;

public class TopListSettings
{
    public TopListSettings()
    {
    }

    public TopListSettings(bool extraLarge, bool billboard, bool discogs = false)
    {
        this.ExtraLarge = extraLarge;
        this.Billboard = billboard;
        this.Discogs = discogs;
    }

    public bool ExtraLarge { get; set; }

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
