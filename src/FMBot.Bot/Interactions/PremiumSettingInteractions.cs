using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class PremiumSettingInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly GuildService _guildService;
    private readonly PremiumSettingBuilder _premiumSettingBuilder;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    public PremiumSettingInteractions(
        GuildService guildService,
        PremiumSettingBuilder premiumSettingBuilder,
        GuildSettingBuilder guildSettingBuilder)
    {
        this._guildService = guildService;
        this._premiumSettingBuilder = premiumSettingBuilder;
        this._guildSettingBuilder = guildSettingBuilder;
    }

    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles(params string[] inputs)
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

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBlockedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildBlockedRoles(params string[] inputs)
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

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBotManagementRoleMenu)]
    [ServerStaffOnly]
    public async Task SetBotManagementRoles(params string[] inputs)
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

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveGuildActivityThreshold()
    {
        await this._guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
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
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
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

        await this._guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }
}
