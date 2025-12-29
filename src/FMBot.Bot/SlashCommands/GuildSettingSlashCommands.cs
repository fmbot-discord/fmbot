using System;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Attributes;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;
using FMBot.Domain.Enums;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class GuildSettingSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly GuildService _guildService;
    private readonly GuildBuilders _guildBuilders;

    private InteractiveService Interactivity { get; }

    public GuildSettingSlashCommands(
        GuildSettingBuilder guildSettingBuilder,
        InteractiveService interactivity,
        UserService userService,
        GuildService guildService,
        GuildBuilders guildBuilders)
    {
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._guildService = guildService;
        this._guildBuilders = guildBuilders;
    }

    [SlashCommand("configuration", "Server configuration for .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task ServerSettingsAsync()
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);

            var response =
                await this._guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, contextUser),
                    guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("members", "Members in this server that use .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync(
        [SlashCommandParameter(Name = "view", Description = "Statistic you want to view")]
        GuildViewType viewType)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
