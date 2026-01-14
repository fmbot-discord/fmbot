using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands.Guild;

[ExcludeFromHelp]
[ModuleName("Premium server settings")]
public class PremiumGuildCommands(
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity,
    GuildService guildService,
    UserService userService,
    AdminService adminService,
    IPrefixService prefixService,
    PremiumSettingBuilder premiumSettingBuilder)
    : BaseCommandModule(botSettings)
{
    private readonly AdminService _adminService = adminService;
    private readonly GuildService _guildService = guildService;
    private readonly UserService _userService = userService;

    private InteractiveService Interactivity { get; } = interactivity;

    [Command("allowedroles", "wkwhitelist", "wkroles", "whoknowswhitelist", "whoknowsroles")]
    [Summary("Sets roles that are allowed to be in server-wide charts")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetAllowedRoles([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = Constants.GetPremiumServer });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("blockedroles", "wkwblacklist", "wkblocklist", "whoknowsblaccklist", "whoknowsblocklist")]
    [Summary("Sets roles that are blocked from server-wide charts")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetBlockedRoles([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = Constants.GetPremiumServer });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botmanagementroles", "managementroles", "staffroles", "adminroles", "modroles", "botroles", "botmangementroles")]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetBotManagementRoles([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = Constants.GetPremiumServer });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botmanagementroles", "managementroles", "staffroles", "adminroles", "modroles", "botroles", "botmangementroles")]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetUserActivityThreshold([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = Constants.GetPremiumServer });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("serveractivitythreshold", "activitythreshold", "guildactivitythreshold", "activitytreshold")]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetGuildActivityThreshold([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = Constants.GetPremiumServer });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
