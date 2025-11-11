using System.ComponentModel;
using System.IO;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using NetCord.Rest;

namespace FMBot.Bot.Models;

public class ResponseModel
{
    public ResponseModel()
    {
        this.CommandResponse = CommandResponse.Ok;
        this.Embed = new EmbedProperties()
            .WithColor(DiscordConstants.LastFmColorRed);
        this.EmbedAuthor = new EmbedAuthorProperties();
        this.EmbedFooter = new EmbedFooterProperties();
        this.Spoiler = false;
        this.ReferencedMusic = null;
        this.ComponentsV2 = new ComponentProperties();
        this.ComponentsContainer = new ContainerProperties();
        this.ComponentsV2.WithContainer(this.ComponentsContainer);
    }

    public EmbedAuthorProperties EmbedAuthor { get; set; }
    public EmbedProperties Embed { get; set; }
    public EmbedFooterProperties EmbedFooter { get; set; }
    public ActionRowProperties Components { get; set; }

    public ComponentBuilderV2 ComponentsV2 { get; set; }
    public ComponentContainerProperties ComponentsContainer { get; set; }

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
