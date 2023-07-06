using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Crowns")]
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
        CrownBuilders crownBuilders) : base(botSettings)
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
    }

    [Command("crowns", RunMode = RunMode.Async)]
    [Summary("Shows you your crowns for this server.")]
    [Alias("cws")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task UserCrownsAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (guild.CrownsDisabled == true)
        {
            await ReplyAsync("Crown functionality has been disabled in this server.");
            this.Context.LogCommandUsed(CommandResponse.Disabled);
            return;
        }

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        var crownViewSettings = new CrownViewSettings
        {
            CrownOrderType = CrownOrderType.Playcount
        };

        crownViewSettings = SettingService.SetCrownViewSettings(crownViewSettings, userSettings.NewSearchValue);
        var userCrowns = await this._crownService.GetCrownsForUser(guild, userSettings.UserId, crownViewSettings.CrownOrderType);

        var title = userSettings.DifferentUser
            ? $"Crowns for {userSettings.UserNameLastFm}, requested by {userTitle}"
            : $"Crowns for {userTitle}";

        if (!userCrowns.Any())
        {
            this._embed.WithDescription($"You or the user you're searching for don't have any crowns yet. \n" +
                                        $"Use `{prfx}whoknows` to start getting crowns!");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
            return;
        }

        try
        {
            var pages = new List<PageBuilder>();

            var crownPages = userCrowns.ChunkBy(10);

            var counter = 1;
            var pageCounter = 1;
            foreach (var crownPage in crownPages)
            {
                var crownPageString = new StringBuilder();
                foreach (var userCrown in crownPage)
                {
                    crownPageString.AppendLine($"{counter}. **{userCrown.ArtistName}** - **{userCrown.CurrentPlaycount}** plays (claimed <t:{((DateTimeOffset)userCrown.Created).ToUnixTimeSeconds()}:R>)");
                    counter++;
                }

                var footer = new StringBuilder();

                footer.AppendLine($"Page {pageCounter}/{crownPages.Count} - {userCrowns.Count} total crowns");

                footer.AppendLine(crownViewSettings.CrownOrderType == CrownOrderType.Playcount
                    ? "Ordered by playcount - Available options: playcount and recent"
                    : "Ordered by recent crowns - Available options: playcount and recent");

                pages.Add(new PageBuilder()
                    .WithDescription(crownPageString.ToString())
                    .WithTitle(title)
                    .WithFooter(footer.ToString()));
                pageCounter++;
            }

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        this.Context.LogCommandUsed();
    }

    [Command("crown", RunMode = RunMode.Async)]
    [Summary("Shows crown history for the artist you're currently listening to or searching for")]
    [Alias("cw")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildWithGuildUsers(this.Context.Guild.Id);

        var response = await this._crownBuilders.CrownAsync(new ContextModel(this.Context, prfx, contextUser), guild, artistValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("crownleaderboard", RunMode = RunMode.Async)]
    [Summary("Shows users with the most crowns in your server")]
    [Alias("cwlb", "crownlb", "cwleaderboard", "crown leaderboard")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownLeaderboardAsync()
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (guild.CrownsDisabled == true)
        {
            await ReplyAsync("Crown functionality has been disabled in this server.");
            this.Context.LogCommandUsed(CommandResponse.Disabled);
            return;
        }

        var topCrownUsers = await this._crownService.GetTopCrownUsersForGuild(guild.GuildId);
        var guildCrownCount = await this._crownService.GetTotalCrownCountForGuild(guild.GuildId);
        var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

        if (!topCrownUsers.Any())
        {
            this._embed.WithDescription($"No top crown users in this server. Use whoknows to start getting crowns!");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            return;
        }

        var pages = new List<PageBuilder>();

        var title = $"Users with most crowns in {this.Context.Guild.Name}";

        var crownPages = topCrownUsers.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var crownPage in crownPages)
        {
            var crownPageString = new StringBuilder();
            foreach (var crownUser in crownPage)
            {
                guildUsers.TryGetValue(crownUser.Key, out var guildUser);

                string name = null;

                if (guildUser != null)
                {
                    name = guildUser.UserName;
                }

                crownPageString.AppendLine($"{counter}. **{name ?? crownUser.First().User.UserNameLastFM}** - **{crownUser.Count()}** {StringExtensions.GetCrownsString(crownUser.Count())}");
                counter++;
            }

            var footer = $"Page {pageCounter}/{crownPages.Count} - {guildCrownCount} total active crowns in this server";

            pages.Add(new PageBuilder()
                .WithDescription(crownPageString.ToString())
                .WithTitle(title)
                .WithFooter(footer));
            pageCounter++;
        }

        var paginator = StringService.BuildStaticPaginator(pages);

        _ = this.Interactivity.SendPaginatorAsync(
            paginator,
            this.Context.Channel,
            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

        this.Context.LogCommandUsed();
    }
}
