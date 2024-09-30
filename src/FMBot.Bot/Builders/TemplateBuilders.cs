using System;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class TemplateBuilders
{
    private readonly TemplateService _templateService;

    public TemplateBuilders(TemplateService templateService)
    {
        this._templateService = templateService;
    }

    public async Task<ResponseModel> TemplatePicker(
        ContextModel context,
        Guild guild = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var templates = await this._templateService.GetTemplates(context.ContextUser.UserId);

        var templateManagePicker = new SelectMenuBuilder()
            .WithPlaceholder("Select template to change")
            .WithCustomId(InteractionConstants.Template.ManagePicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var template in templates)
        {
            templateManagePicker.AddOption(new SelectMenuOptionBuilder(template.Name, $"{InteractionConstants.Template.ManagePicker}-{template.Id}"));
        }

        var templateGlobalPicker = new SelectMenuBuilder()
            .WithPlaceholder("Template you use globally")
            .WithCustomId(InteractionConstants.Template.SetGlobalDefaultPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        var templateGuildPicker = new SelectMenuBuilder()
            .WithPlaceholder("Template you use in this server")
            .WithCustomId(InteractionConstants.Template.SetGuildDefaultPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        response.Embed.WithTitle("Manage templates");
        response.Embed.WithDescription("Select the template you want to change, or pick which one you want used as a default.");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder()
            .WithSelectMenu(templateManagePicker)
            .WithButton("Create", InteractionConstants.Template.Create, ButtonStyle.Secondary)
            .WithButton("Import sharecode", InteractionConstants.Template.ImportCode, ButtonStyle.Secondary)
            .WithButton("Import script", InteractionConstants.Template.ImportScript, ButtonStyle.Secondary);

        return response;
    }

    public async Task<ResponseModel> TemplateManage(
        ContextModel context,
        int templateId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var template = await this._templateService.GetTemplate(templateId);

        var templateOptionPicker = new SelectMenuBuilder()
            .WithPlaceholder("Select embed option you want to change")
            .WithCustomId(InteractionConstants.Template.SetOptionPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((EmbedOption[])Enum.GetValues(typeof(EmbedOption))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var value = Enum.GetName(option);

            var description = "Not set";

            templateOptionPicker.AddOption(new SelectMenuOptionBuilder(name, value, description));
        }

        response.Components = new ComponentBuilder()
            .WithSelectMenu(templateOptionPicker)
            .WithButton("Rename", $"{InteractionConstants.Template.Rename}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Copy", $"{InteractionConstants.Template.Copy}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Delete", $"{InteractionConstants.Template.Delete}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Edit script", $"{InteractionConstants.Template.ViewScript}-{template.Id}", ButtonStyle.Secondary);

        response.Embed.WithTitle($"Editing template '{template.Name}'");

        return response;
    }

    public static ResponseModel TemplatesSupporterRequired(ContextModel context, string prfx)
    {
        if (SupporterService.IsSupporter(context.ContextUser.UserType))
        {
            return null;
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription($"Only supporters can configure templates and fully customize their fm commands.\n\n" +
                                       $"[Get supporter here]({Constants.GetSupporterDiscordLink}).");

        response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link,
            url: Constants.GetSupporterDiscordLink);

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.CommandResponse = CommandResponse.SupporterRequired;

        return response;
    }
}
