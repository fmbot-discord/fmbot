namespace FMBot.Domain.Models;

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

public class TasteMatch
{
    public TasteMatch(string name, long ownPlaycount, long otherPlaycount)
    {
        this.Name = name;
        this.OwnPlaycount = ownPlaycount;
        this.OtherPlaycount = otherPlaycount;
    }

    public string Name { get; }

    public long OwnPlaycount { get; }

    public long OtherPlaycount { get; }
}
