using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Flags;

[Flags]
public enum AliasOption : int
{
    [Option("Disable in plays", "Disable redirecting artist name in our update process")]
    DisableInPlays = 1 << 1,
    [Option("NoRedirect in Last.fm calls", "When alias used, enable NoRedirect for Last.fm")]
    NoRedirectInLastfmCalls = 1 << 2,

    [Option("(inactive) Apply internally plays", "Apply redirect internally in our update process")]
    ApplyInternallyPlays = 1 << 3,
    [Option("(inactive) Apply internally Last.fm calls", "Apply redirect internally before Last.fm calls")]
    ApplyInternallyLastfmCalls = 1 << 4,
    [Option("Apply internally Last.fm data", "Apply redirect internally to Last.fm data")]
    ApplyInternallyLastfmData = 1 << 5,
}
