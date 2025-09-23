using System.ComponentModel;
using System.IO;
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
        this.Spoiler = false;
        this.ReferencedMusic = null;
        this.ComponentsV2 = new ComponentBuilderV2();
        this.ComponentsContainer = new ContainerBuilder();
        this.ComponentsV2.WithContainer(this.ComponentsContainer);
    }

    public EmbedAuthorBuilder EmbedAuthor { get; set; }
    public EmbedBuilder Embed { get; set; }
    public EmbedFooterBuilder EmbedFooter { get; set; }
    public ComponentBuilder Components { get; set; }

    public ComponentBuilderV2 ComponentsV2 { get; set; }
    public ContainerBuilder ComponentsContainer { get; set; }

    public ResponseType ResponseType { get; set; }

    public Stream Stream { get; set; }
    public string FileName { get; set; }

    public string FileDescription { get; set; } = null;
    public bool Spoiler { get; set; }

    public string Text { get; set; }

    public bool? HintShown { get; set; }

    public int? GameSessionId { get; set; }

    public string[] EmoteReactions { get; set; }

    public StaticPaginatorBuilder StaticPaginator { get; set; }
    
    public ComponentPaginatorBuilder ComponentPaginator { get; set; }

    public CommandResponse CommandResponse { get; set; }

    public ReferencedMusic ReferencedMusic { get; set; }
}
