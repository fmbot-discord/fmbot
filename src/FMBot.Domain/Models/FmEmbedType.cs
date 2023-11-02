using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum FmEmbedType
{
    [Option("Embed", "Last track (default)")]
    [OptionOrder(2)]
    EmbedMini = 0,

    [Option("Embed full", "Two last tracks")]
    [OptionOrder(3)]
    EmbedFull = 1,

    [Option("Text full", "Two last tracks")]
    [OptionOrder(6)]
    TextFull = 2,

    [Option("Text", "Last track")]
    [OptionOrder(5)]
    TextMini = 3,

    [Option("Embed tiny", "Last track, extra compact embed")]
    [OptionOrder(1)]
    EmbedTiny = 4,

    [Option("Text one-line", "One-line with last track. Disables footer options and album")]
    [OptionOrder(4)]
    TextOneLine = 5
}
