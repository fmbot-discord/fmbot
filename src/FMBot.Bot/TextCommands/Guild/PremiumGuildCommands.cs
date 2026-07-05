using System;
using System.Linq;
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
using FMBot.Domain.Enums;
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
    PremiumSettingBuilder premiumSettingBuilder,
    GuildSettingBuilder guildSettingBuilder)
    : BaseCommandModule(botSettings)
{
    private readonly AdminService _adminService = adminService;
    private readonly GuildService _guildService = guildService;
    private readonly UserService _userService = userService;

    private InteractiveService Interactivity { get; } = interactivity;

    [Command("premiumserver", "premium", "getpremium", "getpremiumserver")]
    [Summary("Unlock server-wide perks and automation for this server")]
    [GuildOnly]
    [UsernameSetRequired]
    public async Task PremiumServerAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await premiumSettingBuilder.PremiumServerOverview(
                new ContextModel(this.Context, prfx, contextUser), null, "premiumserver-text");

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("botbranding", "custombranding", "botavatar", "customlogo")]
    [Summary("Give the bot a custom look in this server")]
    [GuildOnly]
    [ExcludeFromHelp]
    public async Task BotBrandingAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.GetPremiumServer });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var context = new ContextModel(this.Context, prfx);

            var attachment = this.Context.Message.Attachments?.FirstOrDefault();
            if (attachment != null)
            {
                if (!await guildSettingBuilder.UserIsAllowed(context))
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId,
                        new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                    return;
                }

                var error = await premiumSettingBuilder.SetGuildBotAvatar(this.Context.Client.Rest,
                    this.Context.Guild, attachment.Url, attachment.Size);

                if (error != null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId,
                        new MessageProperties { Content = error });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
                if (guild.FeaturedMode is null or GuildFeaturedMode.GlobalFeatured)
                {
                    await this._guildService.SetFeaturedModeAsync(this.Context.Guild,
                        GuildFeaturedMode.CustomBotGlobalFeatured);
                }

                await this._guildService.SendBotBrandingAuditLog(
                    $"🖼️ **Custom bot avatar set**\n" +
                    $"Server: **{StringExtensions.Sanitize(this.Context.Guild.Name)}** — `{this.Context.Guild.Id}`\n" +
                    $"By: <@{this.Context.User.Id}> — `{this.Context.User.Username}`",
                    attachment.Url);
            }

            var response = await premiumSettingBuilder.BotBranding(context);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("servershortcuts", "guildshortcuts", "servershortcut", "guildshortcut")]
    [Summary("Manage server-wide command shortcuts")]
    [GuildOnly]
    [UsernameSetRequired]
    public async Task ServerShortcutsAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, prfx, contextUser);

            var premiumRequiredResponse = PremiumSettingBuilder.GuildShortcutsPremiumRequired(context);
            if (premiumRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, premiumRequiredResponse, userService);
                await this.Context.LogCommandUsedAsync(premiumRequiredResponse, userService);
                return;
            }

            var response = await premiumSettingBuilder.ListGuildShortcutsAsync(context);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("allowedroles", "wkwhitelist", "wkroles", "whoknowswhitelist", "whoknowsroles")]
    [Summary("Sets roles that are allowed to be in server-wide charts")]
    [GuildOnly]
    [ExcludeFromHelp]
    [RequiresIndex]
    public async Task SetAllowedRoles([CommandParameter(Remainder = true)] string unused = null)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.GetPremiumServer });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.GetPremiumServer });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.GetPremiumServer });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.GetPremiumServer });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
            return;
        }

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
