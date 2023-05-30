using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class PremiumSettingBuilder
{
    private readonly GuildService _guildService;

    public PremiumSettingBuilder(GuildService guildService)
    {
        this._guildService = guildService;
    }

    public async Task<ResponseModel> AllowedRoles(ContextModel context, IUser lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var fmType = new SelectMenuBuilder()
            .WithPlaceholder("Pick allowed roles")
            .WithCustomId(InteractionConstants.SetAllowedRoleMenu)
            .WithType(ComponentType.RoleSelect)
            .WithMinValues(0)
            .WithMaxValues(25);

        response.Embed.WithTitle("Set server allowed roles");

        var description = new StringBuilder();
        description.AppendLine("Select the roles that you want to be visible in .fmbot.");
        description.AppendLine("This affects WhoKnows, but also all server-wide charts and other commands.");
        description.AppendLine();

        if (guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.AllowedRoles)
            {
                var role = context.DiscordGuild.GetRole(roleId);
                if (role != null)
                {
                    description.AppendLine($"- <@&{roleId}>");
                }
            }
        }
        else
        {
            description.AppendLine($"Picked roles: None");
        }

        response.Embed.WithDescription(description.ToString());

        var footer = new StringBuilder();
        footer.AppendLine("✨ Premium server");
        if (lastModifier != null)
        {
            footer.AppendLine($"Last modified by {lastModifier.Username}");
        }
        response.Embed.WithFooter(footer.ToString());

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder().WithSelectMenu(fmType);

        return response;
    }

    public async Task<ResponseModel> BlockedRoles(ContextModel context, IUser lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var fmType = new SelectMenuBuilder()
            .WithPlaceholder("Pick blocked roles")
            .WithCustomId(InteractionConstants.SetBlockedRoleMenu)
            .WithType(ComponentType.RoleSelect)
            .WithMinValues(0)
            .WithMaxValues(25);

        response.Embed.WithTitle("Set server blocked roles");

        var description = new StringBuilder();
        description.AppendLine("Select the roles that you want to be blocked in .fmbot.");
        description.AppendLine("This affects WhoKnows, but also all server-wide charts and other commands.");
        description.AppendLine();

        if (guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.BlockedRoles)
            {
                var role = context.DiscordGuild.GetRole(roleId);
                if (role != null)
                {
                    description.AppendLine($"- <@&{roleId}>");
                }
            }
        }
        else
        {
            description.AppendLine($"Picked roles: None");
        }

        response.Embed.WithDescription(description.ToString());

        var footer = new StringBuilder();
        footer.AppendLine("✨ Premium server");
        if (lastModifier != null)
        {
            footer.AppendLine($"Last modified by {lastModifier.Username}");
        }
        response.Embed.WithFooter(footer.ToString());

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder().WithSelectMenu(fmType);

        return response;
    }
}
