using FMBot.Domain.Models;

namespace FMBot.Domain.Extensions;

public static class WhoKnowsResponseModeExtensions
{
    public static ResponseMode ToResponseMode(this WhoKnowsResponseMode? mode) => mode switch
    {
        WhoKnowsResponseMode.Image => ResponseMode.Image,
        _ => ResponseMode.Embed
    };
}
