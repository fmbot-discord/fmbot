using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Models;

public sealed class SingleCallbackComponentPaginator(IComponentPaginatorBuilder builder) : ComponentPaginator(builder)
{
    public override async Task<RestMessage> RenderPageAsync(Interaction interaction, InteractionCallbackType responseType,
        bool isEphemeral, IPage page = null)
    {
        if (responseType != InteractionCallbackType.Message)
        {
            return await base.RenderPageAsync(interaction, responseType, isEphemeral, page);
        }

        ArgumentNullException.ThrowIfNull(interaction);

        page ??= await this.PageFactory(this);

        var attachments = page.AttachmentsFactory != null
            ? await page.AttachmentsFactory()
            : Array.Empty<AttachmentProperties>();

        var properties = new InteractionMessageProperties
        {
            Content = page.Text,
            Tts = page.IsTTS,
            Embeds = page.Embeds,
            Components = page.Components,
            AllowedMentions = page.AllowedMentions,
            Attachments = attachments ?? Array.Empty<AttachmentProperties>(),
            Flags = page.MessageFlags
        };

        var callback = await interaction.SendResponseAsync(InteractionCallback.Message(properties), withResponse: true);

        return callback?.Resource?.Message ?? await interaction.GetResponseAsync();
    }
}
