using System.Collections.Generic;
using FMBot.Domain.Enums;

namespace FMBot.Bot.Models;

public class DiscogsCollectionSettings
{
    public DiscogsCollectionSettings()
    {
    }

    public DiscogsCollectionSettings(List<string> formats)
    {
        this.Formats = formats.ConvertAll(format => ToDiscogsFormat(format).format);
    }

    public List<DiscogsFormat> Formats { get; set; }
    public string NewSearchValue { get; set; }

    public static (DiscogsFormat format, string value) ToDiscogsFormat(string format) {
        format = format.ToLower();

        switch (format) {
            case "vinyl" or "vinyls" or "records":
                return (DiscogsFormat.Vinyl, format);
            case "tape" or "tapes" or "cassette" or "cassettes":
                return (DiscogsFormat.Cassette, format);
            case "cd" or "cds":
                return (DiscogsFormat.Cd, format);
            default:
                return (DiscogsFormat.Miscellaneous, null);
        }
    }
}
