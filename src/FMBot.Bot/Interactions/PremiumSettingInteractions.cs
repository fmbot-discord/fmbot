using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class PremiumSettingInteractions(
    UserService userService,
    GuildService guildService,
    PremiumSettingBuilder premiumSettingBuilder,
    GuildSettingBuilder guildSettingBuilder)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildAllowedRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBlockedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildBlockedRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildBlockedRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBotManagementRoleMenu)]
    [ServerStaffOnly]
    public async Task SetBotManagementRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context), managersAllowed: false))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context, managersAllowed: false);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildBotManagementRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveGuildActivityThreshold()
    {
        await guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetGuildActivityThresholdButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetGuildActivityThresholdModal($"{InteractionConstants.SetGuildActivityThresholdModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetGuildActivityThresholdModal)]
    [ServerStaffOnly]
    public async Task SetGuildActivityThreshold(string messageId)
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 999)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `999`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }
}
