using System.Collections.Generic;
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
        this.Components = new ActionRowProperties();
        this.ComponentsV2 = [];
        this.Spoiler = false;
        this.ReferencedMusic = null;
    }

    public EmbedAuthorProperties EmbedAuthor { get; set; }
    public EmbedProperties Embed { get; set; }
    public EmbedFooterProperties EmbedFooter { get; set; }
    public ActionRowProperties Components { get; set; }
    public RoleMenuProperties RoleMenu { get; set; }
    public StringMenuProperties StringMenu { get; set; }

    public List<ActionRowProperties> ComponentsV2 { get; set; }

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
