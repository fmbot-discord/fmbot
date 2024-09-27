using System;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
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

    public ResponseModel TemplatePicker(
        ContextModel context,
        Guild guild = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var templateManagePicker = new SelectMenuBuilder()
            .WithPlaceholder("Select template to change")
            .WithCustomId(InteractionConstants.Template.ManagePicker)
            .WithMinValues(1)
            .WithMaxValues(1);

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

        return response;
    }

    public ResponseModel TemplateManage(
        ContextModel context,
        Template template)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var templateOptionPicker = new SelectMenuBuilder()
            .WithPlaceholder("Select embed option you want to change")
            .WithCustomId(InteractionConstants.Template.SetOptionPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((FmEmbedType[])Enum.GetValues(typeof(EmbedOption))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var value = Enum.GetName(option);

            var description = "Not set";

            templateOptionPicker.AddOption(new SelectMenuOptionBuilder(name, value, description));
        }

        response.Embed.WithTitle($"Editing template '{template.Name}'");

        return response;
    }
}
