using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
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

        var allowedRoles = new SelectMenuBuilder()
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

        if (guild.GuildFlags.HasValue && guild.GuildFlags.Value.HasFlag(GuildFlags.LegacyWhoKnowsWhitelist))
        {
            footer.AppendLine("✨ Grandfathered allowed roles access");
        }
        else
        {
            footer.AppendLine("✨ Premium server");
        }

        if (lastModifier != null)
        {
            footer.AppendLine($"Last modified by {lastModifier.Username}");
        }
        response.Embed.WithFooter(footer.ToString());

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder().WithSelectMenu(allowedRoles);

        return response;
    }

    public async Task<ResponseModel> BlockedRoles(ContextModel context, IUser lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var blockedRoles = new SelectMenuBuilder()
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

        response.Components = new ComponentBuilder().WithSelectMenu(blockedRoles);

        return response;
    }

    public async Task<ResponseModel> BotManagementRoles(ContextModel context, IUser lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var botManagementRoles = new SelectMenuBuilder()
            .WithPlaceholder("Pick bot management roles")
            .WithCustomId(InteractionConstants.SetBotManagementRoleMenu)
            .WithType(ComponentType.RoleSelect)
            .WithMinValues(0)
            .WithMaxValues(25);

        response.Embed.WithTitle("Set bot management roles");

        var description = new StringBuilder();
        description.AppendLine("Select the roles that are allowed to change .fmbot settings on this server.");
        description.AppendLine();
        description.AppendLine("Users with these roles will be able to:");
        description.AppendLine("- Change all bot settings (except for this one)");
        description.AppendLine("- Block and unblock users");
        description.AppendLine("- Run crownseeder");
        description.AppendLine("- Manage crowns");
        description.AppendLine();

        if (guild.BotManagementRoles != null && guild.BotManagementRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.BotManagementRoles)
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

        response.Components = new ComponentBuilder().WithSelectMenu(botManagementRoles);

        return response;
    }

    public async Task<ResponseModel> SetGuildActivityThreshold(ContextModel context, IUser lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithTitle("Set server activity threshold");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var description = new StringBuilder();

        description.AppendLine($"Setting a WhoKnows activity threshold will filter out people who have not talked in your server for a certain amount of days. " +
                               $"A user counts as active as soon as they talk in a channel in which .fmbot has access.");
        description.AppendLine();
        description.AppendLine("This filtering applies to all server-wide commands. " +
                           "The bot only starts tracking user activity after Premium Server has been activated. Any messages before that time are not included.");
        description.AppendLine();

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var components = new ComponentBuilder();

        if (!guild.UserActivityThresholdDays.HasValue)
        {
            description.AppendLine("There is currently no server activity threshold enabled.");
            description.AppendLine("To enable, click the button below and enter the amount of days.");
            components.WithButton("Set server activity threshold", InteractionConstants.SetGuildActivityThreshold, style: ButtonStyle.Secondary);
        }
        else
        {
            var guildMembers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            description.AppendLine($"✅ Enabled.");
            description.AppendLine($"Anyone who hasn't talked in the last **{guild.UserActivityThresholdDays.Value}** days is currently filtered out.");

            var filterDate = DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value);
            var activeUserCount = guildMembers.Count(w => w.Value.LastMessage != null && w.Value.LastMessage >= filterDate);

            description.AppendLine($"The bot has seen **{activeUserCount}** {StringExtensions.GetUsersString(activeUserCount)} talk in this time period.");

            components.WithButton("Remove server activity threshold", $"{InteractionConstants.RemoveGuildActivityThreshold}", style: ButtonStyle.Secondary);
        }

        response.Embed.WithDescription(description.ToString());

        var footer = new StringBuilder();
        footer.AppendLine("✨ Premium server");
        if (lastModifier != null)
        {
            footer.AppendLine($"Last modified by {lastModifier.Username}");
        }
        response.Embed.WithFooter(footer.ToString());

        response.Components = components;

        return response;
    }
}
