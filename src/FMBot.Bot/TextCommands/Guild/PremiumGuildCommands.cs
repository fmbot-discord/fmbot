using System;
using System.Threading.Tasks;
using Discord.Commands;
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

namespace FMBot.Bot.TextCommands.Guild;

[ExcludeFromHelp]
[Name("Premium server settings")]
public class PremiumGuildCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly GuildService _guildService;
    private readonly UserService _userService;

    private readonly PremiumSettingBuilder _premiumSettingBuilder;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }

    public PremiumGuildCommands(
        IOptions<BotSettings> botSettings,
        InteractiveService interactivity,
        GuildService guildService,
        UserService userService,
        AdminService adminService,
        IPrefixService prefixService,
        PremiumSettingBuilder premiumSettingBuilder) : base(botSettings)
    {
        this.Interactivity = interactivity;
        this._guildService = guildService;
        this._userService = userService;
        this._adminService = adminService;
        this._prefixService = prefixService;
        this._premiumSettingBuilder = premiumSettingBuilder;
    }

    [Command("allowedroles", RunMode = RunMode.Async)]
    [Summary("Sets roles that are allowed to be in server-wide charts")]
    [Alias("wkwhitelist", "wkroles", "whoknowswhitelist", "whoknowsroles")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetAllowedRoles([Remainder] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await ReplyAsync(Constants.GetPremiumServer);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("blockedroles", RunMode = RunMode.Async)]
    [Summary("Sets roles that are blocked from server-wide charts")]
    [Alias("wkwblacklist", "wkblocklist", "whoknowsblaccklist", "whoknowsblocklist")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetBlockedRoles([Remainder] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await ReplyAsync(Constants.GetPremiumServer);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botmanagementroles", RunMode = RunMode.Async)]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [Alias("managementroles", "staffroles", "adminroles", "modroles", "botroles", "botmangementroles")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetBotManagementRoles([Remainder] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await ReplyAsync(Constants.GetPremiumServer);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botmanagementroles", RunMode = RunMode.Async)]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [Alias("managementroles", "staffroles", "adminroles", "modroles", "botroles", "botmangementroles")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetUserActivityThreshold([Remainder] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await ReplyAsync(Constants.GetPremiumServer);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("serveractivitythreshold", RunMode = RunMode.Async)]
    [Summary("Sets roles that are allowed to manage .fmbot in this server")]
    [Alias("activitythreshold", "guildactivitythreshold", "activitytreshold")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetGuildActivityThreshold([Remainder] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await ReplyAsync(Constants.GetPremiumServer);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
