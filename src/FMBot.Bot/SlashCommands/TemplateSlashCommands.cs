using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Css;

using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using NetCord;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.SlashCommands;

public class TemplateSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly TemplateBuilders _templateBuilders;
    private readonly TemplateService _templateService;

    private InteractiveService Interactivity { get; }

    public TemplateSlashCommands(UserService userService, TemplateBuilders templateBuilders, GuildService guildService,
        InteractiveService interactivity, TemplateService templateService)
    {
        this._userService = userService;
        this._templateBuilders = templateBuilders;
        this._guildService = guildService;
        this.Interactivity = interactivity;
        this._templateService = templateService;
    }

    [ComponentInteraction(InteractionConstants.Template.ManagePicker)]
    [UsernameSetRequired]
    public async Task TemplateManageAsync(string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

        try
        {
            var templateId = int.Parse(inputs.First().Replace($"{InteractionConstants.Template.ManagePicker}-", ""));

            var response =
                await this._templateBuilders.TemplateManage(new ContextModel(this.Context, contextUser), templateId,
                    guild);

            await this.Context.SendResponse(this.Interactivity, response.response, ephemeral: true,
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

            await this.Context.SendResponse(this.Interactivity, response, true);
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

    [ComponentInteraction($"{InteractionConstants.Template.ViewScript}-*")]
    [UsernameSetRequired]
    public async Task TemplateViewScript(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            var mb = new ModalBuilder()
                .WithTitle($"Template script for '{template.Name}'")
                .WithCustomId($"{InteractionConstants.Template.ViewScriptModal}-{parsedTemplateId}")
                .AddTextInput("Content", "content", TextInputStyle.Paragraph, value: template.Content);

            await Context.Interaction.RespondWithModalAsync(mb);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Template.Rename}-*")]
    [UsernameSetRequired]
    public async Task TemplateRename(string templateId)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            var mb = new ModalBuilder()
                .WithTitle($"Renaming template '{template.Name}'")
                .WithCustomId($"{InteractionConstants.Template.RenameModal}-{parsedTemplateId}")
                .AddTextInput("Name", "name", value: template.Name, maxLength: 32);

            await Context.Interaction.RespondWithModalAsync(mb);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Template.Delete}-*")]
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
                .WithButton("Yes, delete", $"{InteractionConstants.Template.DeleteConfirmed}-{templateId}", ButtonStyle.Danger);

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

    [ComponentInteraction($"{InteractionConstants.Template.DeleteConfirmed}-*")]
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
    public async Task SetOption(string[] inputs)
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out EmbedOption embedOption))
        {

        }
    }
}
