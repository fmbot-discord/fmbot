using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Crowns")]
public class CrownCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly CrownService _crownService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly IPrefixService _prefixService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly ArtistsService _artistsService;
    private readonly CrownBuilders _crownBuilders;
    private readonly GuildBuilders _guildBuilders;

    private InteractiveService Interactivity { get; }

    public CrownCommands(CrownService crownService,
        GuildService guildService,
        IPrefixService prefixService,
        UserService userService,
        AdminService adminService,
        IDataSourceFactory dataSourceFactory,
        SettingService settingService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        ArtistsService artistsService,
        CrownBuilders crownBuilders,
        GuildBuilders guildBuilders) : base(botSettings)
    {
        this._crownService = crownService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._userService = userService;
        this._adminService = adminService;
        this._dataSourceFactory = dataSourceFactory;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._artistsService = artistsService;
        this._crownBuilders = crownBuilders;
        this._guildBuilders = guildBuilders;
    }

    [Command("crowns", "cws", "topcrowns", "topcws", "tcws")]
    [Summary("Shows you your crowns for this server.")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task UserCrownsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var crownViewType = SettingService.SetCrownViewSettings(userSettings.NewSearchValue);

        try
        {
            var response = await this._crownBuilders.CrownOverviewAsync(new ContextModel(this.Context, prfx, contextUser), guild, userSettings, crownViewType);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crown", "cw")]
    [Summary("Shows crown history for the artist you're currently listening to or searching for")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownAsync(new ContextModel(this.Context, prfx, contextUser), guild, artistValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crownleaderboard", "cwlb", "crownlb", "cwleaderboard")]
    [Summary("Shows users with the most crowns in your server")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownLeaderboardAsync()
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                guild, GuildViewType.Crowns);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
