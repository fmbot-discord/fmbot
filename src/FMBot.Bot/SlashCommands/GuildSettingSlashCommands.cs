using System;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Attributes;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class GuildSettingSlashCommands(
    GuildSettingBuilder guildSettingBuilder,
    InteractiveService interactivity,
    UserService userService,
    GuildService guildService,
    GuildBuilders guildBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("configuration", "Server configuration for .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task ServerSettingsAsync()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);

            var response =
                await guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, contextUser),
                    guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
