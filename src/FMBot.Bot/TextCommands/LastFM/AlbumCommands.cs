using System;
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
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Constants = FMBot.Domain.Constants;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Albums")]
public class AlbumCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly PlayBuilder _playBuilders;

    private InteractiveService Interactivity { get; }

    public AlbumCommands(
        GuildService guildService,
        IndexService indexService,
        IPrefixService prefixService,
        SettingService settingService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        AlbumBuilders albumBuilders,
        PlayBuilder playBuilder) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._albumBuilders = albumBuilders;
        this._playBuilders = playBuilder;
    }

    [Command("album", RunMode = RunMode.Async)]
    [Summary("Shows album you're currently listening to or searching for.")]
    [Examples(
        "ab",
        "album",
        "album Ventura Anderson .Paak",
        "ab Boy Harsher | Yr Body Is Nothing")]
    [Alias("ab")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    [SupporterEnhanced("Supporters can see the date they first discovered an album")]
    public async Task AlbumAsync([Remainder] string albumValues = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response =
                await this._albumBuilders.AlbumAsync(new ContextModel(this.Context, prfx, contextUser), albumValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("albumplays", RunMode = RunMode.Async)]
    [Summary("Shows playcount for current album or the one you're searching for.\n\n" +
             "You can also mention another user to see their playcount.")]
    [Examples(
        "abp",
        "albumplays",
        "albumplays @user",
        "albumplays lfm:fm-bot",
        "albumplays The Slow Rush",
        "abp The Beatles | Yesterday",
        "abp The Beatles | Yesterday @user")]
    [Alias("abp", "albumplay", "abplays", "albump", "album plays")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumPlaysAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._albumBuilders.AlbumPlaysAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("cover", RunMode = RunMode.Async)]
    [Summary("Cover for current album or the one you're searching for.")]
    [Examples(
        "co",
        "cover",
        "cover la priest inji")]
    [Alias("abc", "abco", "co", "albumcover", "album cover")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Charts)]
    public async Task AlbumCoverAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);

        try
        {
            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, albumValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("topalbums", RunMode = RunMode.Async)]
    [Summary("Shows your or someone else's top albums over a certain time period.")]
    [Options(Constants.CompactTimePeriodList,
        "Albums released in year: `r:2023`, `released:2023`",
        "Albums released in decade: `d:80s`, `decade:1990`",
        Constants.UserMentionExample, Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("tab", "topalbums", "tab a lfm:fm-bot", "topalbums weekly @user", "tab bb xl")]
    [Alias("abl", "abs", "tab", "albumlist", "top albums", "albums", "albumslist", "talbum")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    public async Task TopAlbumsAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue,
                topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue
                    ? TimePeriod.AllTime
                    : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var mode = SettingService.SetMode(timeSettings.NewSearchValue, contextUser.Mode);

            var response = await this._albumBuilders.TopAlbumsAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("whoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to an album in your server")]
    [Alias("wa", "wka", "wkab", "wab", "wkab", "wk album", "whoknows album")]
    [Examples("wa", "whoknowsalbum", "whoknowsalbum the beatles abbey road",
        "whoknowsalbum Metallica & Lou Reed | Lulu")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task WhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = albumValues,
                DisplayRoleFilter = false
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await this._albumBuilders.WhoKnowsAlbumAsync(
                new ContextModel(this.Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("globalwhoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to the an album on .fmbot")]
    [Examples("gwa", "globalwhoknowsalbum", "globalwhoknowsalbum the beatles abbey road",
        "globalwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [Alias("gwa", "gwka", "gwab", "gwkab", "globalwka", "globalwkalbum", "globalwhoknows album")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel.TriggerTypingAsync();
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = albumValues
        };

        var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

        try
        {
            var response = await this._albumBuilders.GlobalWhoKnowsAlbumAsync(
                new ContextModel(this.Context, prfx, contextUser), settings, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("friendwhoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Who of your friends listen to an album")]
    [Examples("fwa", "fwka COMA", "friendwhoknows", "friendwhoknowsalbum the beatles abbey road",
        "friendwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [Alias("fwa", "fwka", "fwkab", "fwab", "friendwhoknows album", "friends whoknows album", "friend whoknows album")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = albumValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await this._albumBuilders
                .FriendsWhoKnowAlbumAsync(new ContextModel(this.Context, prfx, contextUser),
                    currentSettings.ResponseMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("albumtracks", RunMode = RunMode.Async)]
    [Summary("Shows track playcounts for a specific album")]
    [Examples("abt", "albumtracks", "albumtracks de jeugd van tegenwoordig machine",
        "albumtracks U2 | The Joshua Tree")]
    [Alias("abt", "abtracks", "albumt")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumTracksAsync([Remainder] string albumValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);

            var response = await this._albumBuilders.AlbumTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, userSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("serveralbums", RunMode = RunMode.Async)]
    [Summary("Top albums for your server, optionally for a specific artist.")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`",
        "Artist name")]
    [Examples("sab", "sab a p", "serveralbums", "serveralbums alltime", "serveralbums listeners weekly",
        "serveralbums the beatles monthly")]
    [Alias("sab", "stab", "servertopalbums", "serveralbum", "server albums")]
    [RequiresIndex]
    [GuildOnly]
    [CommandCategories(CommandCategory.Albums)]
    public async Task GuildAlbumsAsync([Remainder] string guildAlbumsOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = guildAlbumsOptions
        };

        try
        {
            guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, guildAlbumsOptions);
            var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue,
                guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

            if (timeSettings.UsePlays ||
                timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
            {
                guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
            }

            var response =
                await this._albumBuilders.GuildAlbumsAsync(new ContextModel(this.Context, prfx), guild,
                    guildListSettings);

            _ = this.Interactivity.SendPaginatorAsync(
                response.StaticPaginator.Build(),
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("albumgaps", RunMode = RunMode.Async)]
    [Summary("Shows the albums you've returned to after a gap in listening.")]
    [Options(Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("agaps", "albumgaps", "albumgaps @user")]
    [Alias("agaps", "agap", "abgaps", "abgap", "albumgap")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    [SupporterExclusive("To see which albums you've re-discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task AlbumGapsAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var supporterRequiredResponse = PlayBuilder.GapsSupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var mode = SettingService.SetMode(userSettings.NewSearchValue, contextUser.Mode);

            var response = await this._playBuilders.ListeningGapsAsync(context, topListSettings, userSettings,
                mode.mode, GapEntityType.Album);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
