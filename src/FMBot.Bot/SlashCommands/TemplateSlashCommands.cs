using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Css;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.SlashCommands;

public class TemplateSlashCommands : InteractionModuleBase
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

            await Context.Interaction.RespondWithModalAsync(mb.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ModalInteraction($"{InteractionConstants.Template.ViewScriptModal}-*")]
    [UsernameSetRequired]
    public async Task TemplateViewScriptSubmit(string templateId, TemplateViewScriptModal modal)
    {
        try
        {
            var parsedTemplateId = int.Parse(templateId);
            var template = await this._templateService.GetTemplate(parsedTemplateId);

            await this._templateService.UpdateTemplate(template.Id, modal.Content);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
