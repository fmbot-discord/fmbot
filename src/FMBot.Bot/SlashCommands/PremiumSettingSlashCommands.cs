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

namespace FMBot.Bot.SlashCommands;

public class PremiumSettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private readonly PremiumSettingBuilder _premiumSettingBuilder;

    public PremiumSettingSlashCommands(UserService userService,
        GuildService guildService,
        PremiumSettingBuilder premiumSettingBuilder)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._premiumSettingBuilder = premiumSettingBuilder;
    }

    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles(string[] inputs)
    {
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
}
