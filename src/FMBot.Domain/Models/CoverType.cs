using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

public enum CoverType
{
    [Option("Motion", "Show animated covers when available (default)")]
    Motion = 1,

    [Option("Still", "Always show the static album cover")]
    Still = 2
}
