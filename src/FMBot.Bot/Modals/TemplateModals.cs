using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class TemplateModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly TemplateService _templateService;

    public TemplateModals(TemplateService templateService)
    {
        this._templateService = templateService;
    }

    [ComponentInteraction($"{InteractionConstants.Template.ViewScriptModal}:*")]
    [UsernameSetRequired]
    public async Task TemplateViewScriptSubmit(string templateId)
    {
        try
        {
            var content = this.Context.GetModalValue("content");
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            await this._templateService.UpdateTemplateContent(template.Id, content);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Template.RenameModal}:*")]
    [UsernameSetRequired]
    public async Task TemplateRenameSubmit(string templateId)
    {
        try
        {
            var name = this.Context.GetModalValue("name");
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            await this._templateService.UpdateTemplateName(template.Id, name);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
