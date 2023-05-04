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

    private readonly GuildSettingBuilder _guildSettingBuilder;

    public PremiumSettingSlashCommands(UserService userService,
        GuildService guildService,
        GuildSettingBuilder guildSettingBuilder)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._guildSettingBuilder = guildSettingBuilder;
    }

    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles(string[] inputs)
    {
        if (inputs != null && inputs.Any())
        {
            var roleIds = new List<ulong>();
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }

            await this._guildService.ChangeGuildAllowedRoles(this.Context.Guild, roleIds.ToArray());
        }


        var response = await this._guildSettingBuilder.AllowedRoles(new ContextModel(this.Context));

        await this.Context.UpdateInteractionEmbed(response);
    }
}
