using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum DataSource
{
    [Option("Last.fm", "Only use Last.fm")]
    LastFm = 1,
    [Option("Full Imports, then Last.fm", "Use your full imported history and add Last.fm afterwards")]
    FullImportThenLastFm = 2,
    [Option("Import until full Last.fm", "Use your imported history up until you started scrobbling")]
    ImportThenFullLastFm = 3
}
