using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Discord.WebSocket;
using FMBot.Bot.Models.Modals;

namespace FMBot.Bot.SlashCommands;

public class PremiumSettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private readonly PremiumSettingBuilder _premiumSettingBuilder;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    public PremiumSettingSlashCommands(UserService userService,
        GuildService guildService,
        PremiumSettingBuilder premiumSettingBuilder,
        GuildSettingBuilder guildSettingBuilder)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._premiumSettingBuilder = premiumSettingBuilder;
        this._guildSettingBuilder = guildSettingBuilder;
    }

    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context))) 
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (inputs != null)
        {
            var roleIds = new List<ulong>();
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }

            await this._guildService.ChangeGuildAllowedRoles(this.Context.Guild, roleIds.ToArray());
        }

        var response = await this._premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context), this.Context.User);

        await this.DeferAsync();

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBlockedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildBlockedRoles(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (inputs != null)
        {
            var roleIds = new List<ulong>();
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }

            await this._guildService.ChangeGuildBlockedRoles(this.Context.Guild, roleIds.ToArray());
        }

        var response = await this._premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context), this.Context.User);

        await this.DeferAsync();

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBotManagementRoleMenu)]
    [ServerStaffOnly]
    public async Task SetBotManagementRoles(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context), managersAllowed: false))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context, managersAllowed: false);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (inputs != null)
        {
            var roleIds = new List<ulong>();
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }

            await this._guildService.ChangeGuildBotManagementRoles(this.Context.Guild, roleIds.ToArray());
        }

        var response = await this._premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context), this.Context.User);

        await this.DeferAsync();

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetGuildActivityThreshold()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<SetGuildActivityThresholdModal>($"{InteractionConstants.SetGuildActivityThresholdModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.SetGuildActivityThresholdModal}-*")]
    [ServerStaffOnly]
    public async Task SetGuildActivityThreshold(string messageId, SetGuildActivityThresholdModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        if (!int.TryParse(modal.Amount, out var result) ||
            result < 2 ||
            result > 999)
        {
            await RespondAsync($"Please enter a valid number between `2` and `999`.", ephemeral: true);
            return;
        }

        await this._guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.RemoveGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveGuildActivityThreshold()
    {
        await this._guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }
}
