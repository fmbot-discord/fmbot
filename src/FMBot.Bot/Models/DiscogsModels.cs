using System.Collections.Generic;
using System.Linq;

namespace FMBot.Bot.Models;


public enum DiscogsFormat {
    Vinyl,
    Cassette,
    Cd,
    Miscellaneous,
    Unknown,
}

public class DiscogsCollectionSettings
{
    public DiscogsCollectionSettings()
    {
    }

    public DiscogsCollectionSettings(List<string> formats)
    {
        this.Formats = formats.ConvertAll(format => ToDiscogsFormat(format));
    }

    public List<DiscogsFormat> Formats { get; set; }
    public string NewSearchValue { get; set; }

    public static DiscogsFormat ToDiscogsFormat(string format) {
        switch (format) {
            case "vinyl" or "vinyls" or "records":
                return DiscogsFormat.Vinyl;
            case "tape" or "tapes" or "cassette" or "cassettes":
                return DiscogsFormat.Cassette;
            case "cd" or "CD" or "cds" or "CDs":
                return DiscogsFormat.Cd;
            case "misc" or "miscellaneous":
                return DiscogsFormat.Miscellaneous;
            default:
                return DiscogsFormat.Unknown;
        }
    }
}
