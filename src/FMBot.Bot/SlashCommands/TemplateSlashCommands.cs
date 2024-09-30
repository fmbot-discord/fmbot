using System;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Css;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;

namespace FMBot.Bot.SlashCommands;

public class TemplateSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly TemplateBuilders _templateBuilders;

    private InteractiveService Interactivity { get; }

    public TemplateSlashCommands(UserService userService, TemplateBuilders templateBuilders, GuildService guildService, InteractiveService interactivity)
    {
        this._userService = userService;
        this._templateBuilders = templateBuilders;
        this._guildService = guildService;
        this.Interactivity = interactivity;
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

            var response = await this._templateBuilders.TemplateManage(new ContextModel(this.Context, contextUser), templateId);

            await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

}
