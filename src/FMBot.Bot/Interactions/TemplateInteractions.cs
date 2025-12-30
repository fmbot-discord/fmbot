using System;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class TemplateInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly TemplateBuilders _templateBuilders;
    private readonly TemplateService _templateService;
    private readonly InteractiveService _interactivity;

    public TemplateInteractions(
        UserService userService,
        GuildService guildService,
        TemplateBuilders templateBuilders,
        TemplateService templateService,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._templateBuilders = templateBuilders;
        this._templateService = templateService;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.Template.ManagePicker)]
    [UsernameSetRequired]
    public async Task TemplateManageAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValue = stringMenuInteraction.Data.SelectedValues[0];

        try
        {
            var templateId = int.Parse(selectedValue.Replace($"{InteractionConstants.Template.ManagePicker}:", ""));

            var response =
                await this._templateBuilders.TemplateManage(new ContextModel(this.Context, contextUser), templateId,
                    guild);

            await this.Context.SendResponse(this._interactivity, response.response, ephemeral: true,
                response.extraResponse);
            this.Context.LogCommandUsed(response.response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.ViewVariables)]
    [UsernameSetRequired]
    public async Task TemplateViewVariables()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = TemplateBuilders.GetTemplateVariables(new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(this._interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Template.Create}")]
    [UsernameSetRequired]
    public async Task TemplateCreate()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var newTemplate = await this._templateService.CreateTemplate(contextUser.UserId);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.Delete)]
    [UsernameSetRequired]
    public async Task TemplateDelete(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            var embed = new EmbedProperties()
                .WithColor(DiscordConstants.WarningColorOrange)
                .WithDescription($"Are you sure you want to delete **{template.Name}**?");

            var components = new ActionRowProperties()
                .WithButton("Yes, delete", $"{InteractionConstants.Template.DeleteConfirmed}:{templateId}", ButtonStyle.Danger);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithComponents([components])
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.DeleteConfirmed)]
    [UsernameSetRequired]
    public async Task TemplateDeleteConfirmed(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            await this._templateService.DeleteTemplate(parsedTemplateId);

            var embed = new EmbedProperties()
                .WithColor(DiscordConstants.WarningColorOrange)
                .WithDescription("Template has been deleted.");

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.SetOptionPicker)]
    [UsernameSetRequired]
    public async Task SetOption()
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValue = stringMenuInteraction.Data.SelectedValues[0];

        if (Enum.TryParse(selectedValue, out EmbedOption embedOption))
        {

        }
    }

    [ComponentInteraction(InteractionConstants.Template.ViewScript)]
    [UsernameSetRequired]
    public async Task TemplateViewScriptButton(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateTemplateViewScriptModal(
                    $"{InteractionConstants.Template.ViewScriptModal}:{parsedTemplateId}",
                    $"Template script for '{template.Name}'",
                    template.Content)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.Rename)]
    [UsernameSetRequired]
    public async Task TemplateRenameButton(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateTemplateNameModal(
                    $"{InteractionConstants.Template.RenameModal}:{parsedTemplateId}",
                    $"Renaming template '{template.Name}'",
                    template.Name)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Template.ViewScriptModal)]
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

    [ComponentInteraction(InteractionConstants.Template.RenameModal)]
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
