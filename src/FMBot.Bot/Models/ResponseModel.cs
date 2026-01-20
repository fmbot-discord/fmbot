using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        this.Components = [];
        this.ButtonRows = [];
        this.ComponentsV2 = [];
        this.ComponentsContainer = new ComponentContainerProperties();
        this.StringMenus = [];
        this.Spoiler = false;
        this.ReferencedMusic = null;
    }

    public EmbedAuthorProperties EmbedAuthor { get; set; }
    public EmbedProperties Embed { get; set; }
    public EmbedFooterProperties EmbedFooter { get; set; }
    public ActionRowProperties Components { get; set; }
    public Dictionary<int, ActionRowProperties> ButtonRows { get; set; }
    public RoleMenuProperties RoleMenu { get; set; }
    public List<StringMenuProperties> StringMenus { get; set; }

    public List<ActionRowProperties> ComponentsV2 { get; set; }

    public ComponentContainerProperties ComponentsContainer { get; set; }

    public IMessageComponentProperties[] GetComponentsV2()
    {
        var components = new List<IMessageComponentProperties>();

        // Add ComponentsContainer if it has content
        if (ComponentsContainer?.Any() == true)
        {
            components.Add(ComponentsContainer);
        }

        // Add all ComponentsV2 action rows
        if (ComponentsV2?.Count > 0)
        {
            components.AddRange(ComponentsV2);
        }

        return components.Count > 0 ? components.ToArray() : null;
    }

    public IMessageComponentProperties[] GetMessageComponents()
    {
        const int maxComponentsPerRow = 5;
        var components = new List<IMessageComponentProperties>();

        // Add button rows if any (sorted by row number)
        if (ButtonRows?.Count > 0)
        {
            foreach (var row in ButtonRows.OrderBy(r => r.Key))
            {
                if (row.Value?.Any() == true)
                {
                    var rowComponents = row.Value.ToList();
                    if (rowComponents.Count <= maxComponentsPerRow)
                    {
                        components.Add(row.Value);
                    }
                    else
                    {
                        // Split into multiple rows
                        for (var i = 0; i < rowComponents.Count; i += maxComponentsPerRow)
                        {
                            var chunk = rowComponents.Skip(i).Take(maxComponentsPerRow).ToArray();
                            var newRow = new ActionRowProperties();
                            newRow.AddComponents(chunk);
                            components.Add(newRow);
                        }
                    }
                }
            }
        }
        // Fall back to single Components property for backward compatibility
        else if (Components?.Any() == true)
        {
            var componentsList = Components.ToList();
            if (componentsList.Count <= maxComponentsPerRow)
            {
                components.Add(Components);
            }
            else
            {
                // Split into multiple rows
                for (var i = 0; i < componentsList.Count; i += maxComponentsPerRow)
                {
                    var chunk = componentsList.Skip(i).Take(maxComponentsPerRow).ToArray();
                    var newRow = new ActionRowProperties();
                    newRow.AddComponents(chunk);
                    components.Add(newRow);
                }
            }
        }

        // Add all string menus (each is its own component)
        if (StringMenus?.Count > 0)
        {
            components.AddRange(StringMenus);
        }

        // Add role menu if present
        if (RoleMenu != null)
        {
            components.Add(RoleMenu);
        }

        return components.Count > 0 ? components.ToArray() : null;
    }

    public ResponseType ResponseType { get; set; }

    public Stream Stream { get; set; }
    public string FileName { get; set; }

    public string FileDescription { get; set; } = null;
    public bool Spoiler { get; set; }

    public string Text { get; set; }

    public bool? HintShown { get; set; }

    public int? GameSessionId { get; set; }

    public string[] EmoteReactions { get; set; }

    public ComponentPaginatorBuilder ComponentPaginator { get; set; }

    public CommandResponse CommandResponse { get; set; }

    public ReferencedMusic ReferencedMusic { get; set; }
}
