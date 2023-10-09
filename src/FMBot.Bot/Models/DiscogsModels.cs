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
            case "file" or "files":
                return (DiscogsFormat.File, format);
            case "cdr" or "cdrs":
                return (DiscogsFormat.Cdr, format);
            case "dvd" or "dvds":
                return (DiscogsFormat.Dvd, format);
            case "boxset" or "box set" or "boxsets" or "box sets":
                return (DiscogsFormat.BoxSet, format);
            case "flexi-disc" or "flexidisc" or "flexi-discs" or "flexidiscs":
                return (DiscogsFormat.FlexiDisc, format);
            case "8track" or "8tracks" or "8-track cartridge":
                return (DiscogsFormat.EightTrack, format);
            case "blu-ray" or "bluray" or "blu-rays" or "blurays":
                return (DiscogsFormat.BluRay, format);
            default:
                return (DiscogsFormat.Miscellaneous, null);
        }
    }
}
