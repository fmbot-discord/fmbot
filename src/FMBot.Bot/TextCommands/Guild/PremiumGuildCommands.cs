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

    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public PremiumGuildCommands(
        IOptions<BotSettings> botSettings,
        InteractiveService interactivity,
        GuildService guildService,
        UserService userService,
        GuildSettingBuilder guildSettingBuilder,
        AdminService adminService, IPrefixService prefixService) : base(botSettings)
    {
        this.Interactivity = interactivity;
        this._guildService = guildService;
        this._userService = userService;
        this._guildSettingBuilder = guildSettingBuilder;
        this._adminService = adminService;
        this._prefixService = prefixService;
    }

    [Command("allowedroles", RunMode = RunMode.Async)]
    [Summary("Sets roles that are allowed to be in server-wide charts")]
    [Alias("wkwhitelist", "wkroles", "whoknowswhitelist", "whoknowsroles")]
    [GuildOnly]
    [ExcludeFromHelp]
    public async Task SetAllowedRoles([Remainder] string unused = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._guildSettingBuilder.AllowedRoles(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }

    }
}
