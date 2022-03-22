using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Builders;

namespace FMBot.Bot.SlashCommands;

[Group("whoknows", "Whoknows commands")]
public class WhoKnowsCommands
{
    private readonly AlbumBuilders _albumBuilders;

    private InteractiveService Interactivity { get; }

    public WhoKnowsCommands(AlbumBuilders albumBuilders, InteractiveService interactivity)
    {
        this._albumBuilders = albumBuilders;
        this.Interactivity = interactivity;
    }


}
