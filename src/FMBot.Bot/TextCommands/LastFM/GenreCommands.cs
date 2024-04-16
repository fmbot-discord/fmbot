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
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

public class GenreCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GenreService _genreService;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly GenreBuilders _genreBuilders;

    private InteractiveService Interactivity { get; }

    public GenreCommands(
        IPrefixService prefixService,
        IOptions<BotSettings> botSettings,
        UserService userService,
        SettingService settingService,
        IDataSourceFactory dataSourceFactory,
        InteractiveService interactivity,
        GenreService genreService,
        ArtistsService artistsService,
        GuildService guildService,
        IIndexService indexService,
        GenreBuilders genreBuilders) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this._settingService = settingService;
        this._dataSourceFactory = dataSourceFactory;
        this.Interactivity = interactivity;
        this._genreService = genreService;
        this._artistsService = artistsService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._genreBuilders = genreBuilders;
    }

    [Command("topgenres", RunMode = RunMode.Async)]
    [Summary("Shows a list of your or someone else's top genres over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("tg", "topgenres", "tg a lfm:fm-bot", "topgenres weekly @user")]
    [Alias("gl", "tg", "genrelist", "genres", "top genres", "genreslist")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    public async Task TopGenresAsync([Remainder] string extraOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);

            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await this._genreBuilders.GetTopGenres(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings, mode.mode);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("genre", RunMode = RunMode.Async)]
    [Summary("Shows genre information for an artist, or top artist for a specific genre")]
    [Examples("genre", "genres hip hop, electronic", "g", "genre Indie Soul", "genre The Beatles")]
    [Alias("genreinfo", "genres", "gi", "g")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    [GuildOnly]
    public async Task GenreInfoAsync([Remainder] string genreOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var userView = SettingService.IsUserView(genreOptions);

        try
        {
            var userSettings = await this._settingService.GetUser(userView.NewSearchValue, contextUser, this.Context);
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, prfx, contextUser), userSettings.NewSearchValue, userSettings, guild, userView.User);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("whoknowsgenre", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to a genre in your server")]
    [Examples("wg", "wkg hip hop", "whoknowsgenre", "whoknowsgenre techno")]
    [Alias("wg", "wkg", "whoknows genre")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Genres, CommandCategory.WhoKnows)]
    public async Task WhoKnowsGenreAsync([Remainder] string genreValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._genreBuilders.WhoKnowsGenreAsync(new ContextModel(this.Context, prfx, contextUser), genreValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("servergenres", RunMode = RunMode.Async)]
    [Summary("Top genres for your server")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`")]
    [Examples("sg", "sg a p", "servergenres", "servergenres alltime", "servergenres listeners weekly")]
    [Alias("sg", "sgenres", "serverg", "server genre", "servergenre", "server genres")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Genres)]
    public async Task GuildGenresAsync([Remainder] string extraOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = extraOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, extraOptions);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response = await this._genreBuilders.GetGuildGenres(new ContextModel(this.Context, prfx), guild, guildListSettings);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("friendwhoknowgenre", RunMode = RunMode.Async)]
    [Summary("Shows who of your friends listen to a genre in .fmbot")]
    [Examples("fwg", "fwg pop", "friendwhoknowgenre", "friendwhoknowgenre pov: indie")]
    [Alias("fwg", "fwkg", "friendwhoknows genre", "friendwhoknowsgenre", "friend whoknowsgenre", "friends whoknow genre", "friend whoknows genre", "friends whoknows genre")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAsync([Remainder] string genreValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var response = await this._genreBuilders
                .FriendsWhoKnowsGenreAsync(new ContextModel(this.Context, prfx, contextUser), genreValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }
}
