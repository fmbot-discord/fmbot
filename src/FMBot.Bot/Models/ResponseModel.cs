using Discord;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class ResponseModel
{
    public ResponseModel()
    {
        this.CommandResponse = CommandResponse.Ok;
        this.Embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        this.EmbedAuthor = new EmbedAuthorBuilder();
        this.EmbedFooter = new EmbedFooterBuilder();
    }

    public EmbedAuthorBuilder EmbedAuthor { get; set; }
    public EmbedBuilder Embed { get; set; }
    public EmbedFooterBuilder EmbedFooter { get; set; }

    public ResponseType ResponseType { get; set; }


    public string Text { get; set; }

    public StaticPaginator StaticPaginator { get; set; }

    public CommandResponse CommandResponse { get; set; }
}

