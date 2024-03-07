using System;
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
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Artists")]
public class ArtistCommands : BaseCommandModule
{
    private readonly ArtistBuilders _artistBuilders;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly PlayService _playService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly DiscogsBuilder _discogsBuilders;

    private InteractiveService Interactivity { get; }

    public ArtistCommands(
        ArtistsService artistsService,
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        IDataSourceFactory dataSourceFactory,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        ArtistBuilders artistBuilders,
        DiscogsBuilder discogsBuilders) : base(botSettings)
    {
        this._artistsService = artistsService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._dataSourceFactory = dataSourceFactory;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._artistBuilders = artistBuilders;
        this._discogsBuilders = discogsBuilders;
    }

    [Command("artist", RunMode = RunMode.Async)]
    [Summary("Artist you're currently listening to or searching for.")]
    [Examples(
        "a",
        "artist",
        "a Gorillaz",
        "artist Gamma Intel")]
    [Alias("a", "ai", "artistinfo")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(artistValues);

        var userSettings = await this._settingService.GetUser(artistValues, contextUser, this.Context);

        try
        {
            var response = await this._artistBuilders.ArtistInfoAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("artistoverview", RunMode = RunMode.Async)]
    [Summary("Artist you're currently listening to or searching for.")]
    [Examples(
        "ao",
        "artistoverview",
        "ao Gorillaz",
        "artistoverview Gamma Intel")]
    [Alias("ao", "artist overview", "artistsoverview", "artists overview")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistOverviewAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var userSettings = await this._settingService.GetUser(artistValues, contextUser, this.Context);

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        try
        {
            var response = await this._artistBuilders.ArtistOverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("artisttracks", RunMode = RunMode.Async)]
    [Summary("Top tracks for an artist")]
    [Examples(
        "at",
        "artisttracks",
        "artisttracks DMX")]
    [Alias("at", "att", "artisttrack", "artist track", "artist tracks", "artistrack", "artisttoptracks", "artisttoptrack", "favs")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistTracksAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);
        var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.AllTime, cachedOrAllTimeOnly: true, dailyTimePeriods: false);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, prfx, contextUser), timeSettings,
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("artistalbums", RunMode = RunMode.Async)]
    [Summary("Top albums for an artist.")]
    [Examples(
        "aa",
        "artistalbums",
        "artistalbums The Prodigy")]
    [Alias("aa", "aab", "atab", "artistalbum", "artist album", "artist albums", "artistopalbum", "artisttopalbums", "artisttab")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistAlbumsAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await this._artistBuilders.ArtistAlbumsAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("artistplays", RunMode = RunMode.Async)]
    [Summary("Shows playcount for current artist or the one you're searching for.\n\n" +
             "You can also mention another user to see their playcount.")]
    [Examples(
        "ap",
        "artistplays",
        "albumplays @user",
        "ap lfm:fm-bot",
        "artistplays Mall Grab @user")]
    [Alias("ap", "artist plays")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistPlaysAsync([Remainder] string artistValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await this._artistBuilders.ArtistPlaysAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings,
            redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("artistpace", RunMode = RunMode.Async)]
    [Summary("Shows estimated date you reach a certain amount of plays on an artist")]
    [Options("weekly/monthly", "Optional goal amount: For example `500` or `2k`", Constants.UserMentionExample)]
    [Examples("apc", "apc 1k q", "apc 400 h @user", "artistpace", "artistpace weekly @user 2500")]
    [UsernameSetRequired]
    [Alias("apc", "apace", "artistpc")]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistPaceAsync([Remainder] string extraOptions = null)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);
            var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.Monthly, cachedOrAllTimeOnly: true, timeZone: userSettings.TimeZone);

            if (timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                timeSettings = SettingService.GetTimePeriod("monthly", TimePeriod.Monthly, timeZone: userSettings.TimeZone);
            }

            var response = await this._artistBuilders.ArtistPaceAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, timeSettings.NewSearchValue, null, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("topartists", RunMode = RunMode.Async)]
    [Summary("Shows your or someone else's top artists over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("ta", "topartists", "ta a lfm:fm-bot", "topartists weekly @user", "ta bb xl")]
    [Alias("al", "as", "ta", "artistlist", "artists", "top artists", "artistslist")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Artists)]
    public async Task TopArtistsAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            if (topListSettings.Discogs)
            {
                userSettings.RegisteredLastFm = DateTime.MinValue;
            }

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue, topListSettings.Discogs ? TimePeriod.AllTime : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = topListSettings.Discogs
                ? await this._discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings)
                : await this._artistBuilders.TopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            //if (!userSettings.DifferentUser && timeSettings.TimePeriod == TimePeriod.AllTime)
            //{
            //    await this._smallIndexRepository.UpdateUserArtists(contextUser, artists.Content.TopArtists);
            //}
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("discoveries", RunMode = RunMode.Async)]
    [Summary("Shows the artists you've recently discovered.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("d", "discovered", "ta a lfm:fm-bot", "topartists weekly @user", "ta bb xl")]
    [Alias("d", "discovered", "discovery", "artistdiscoveries", "firstlistened")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistDiscoveriesAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue, TimePeriod.Quarterly, registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(timeSettings.NewSearchValue, contextUser.Mode);

            var response = await this._artistBuilders.ArtistDiscoveriesAsync(context, topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("taste", RunMode = RunMode.Async)]
    [Summary("Compares your top artists, genres and countries to those from another user.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionOrLfmUserNameExample, "Mode: `table` or `embed`", Constants.EmbedSizeExample)]
    [Examples("t frikandel_", "t @user", "taste bitldev", "taste @user monthly embed")]
    [UsernameSetRequired]
    [Alias("t")]
    [CommandCategories(CommandCategory.Artists)]
    public async Task TasteAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var otherUser = await this._settingService.GetUser(extraOptions, userSettings, this.Context, firstOptionIsLfmUsername: true);

        var timeSettings = SettingService.GetTimePeriod(
            otherUser.NewSearchValue,
            TimePeriod.AllTime,
            timeZone: userSettings.TimeZone);

        var tasteSettings = new TasteSettings
        {
            EmbedSize = EmbedSize.Default
        };

        tasteSettings = this._artistsService.SetTasteSettings(tasteSettings, timeSettings.NewSearchValue);

        try
        {
            var response = await this._artistBuilders.TasteAsync(new ContextModel(this.Context, prfx, userSettings),
                tasteSettings, timeSettings, otherUser);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("whoknows", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to an artist in your server")]
    [Examples("w", "wk COMA", "whoknows", "whoknows DJ Seinfeld")]
    [Alias("w", "wk", "whoknows artist")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows)]
    public async Task WhoKnowsAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = artistValues,
                DisplayRoleFilter = false
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context,
                    prfx,
                    contextUser),
                settings.ResponseMode,
                settings.NewSearchValue,
                settings.DisplayRoleFilter,
                redirectsEnabled: settings.RedirectsEnabled);

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

    [Command("globalwhoknows", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to an artist in .fmbot")]
    [Examples("gw", "gwk COMA", "globalwhoknows", "globalwhoknows DJ Seinfeld")]
    [Alias("gw", "gwk", "globalwk", "globalwhoknows artist")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                HidePrivateUsers = false,
                ShowBotters = false,
                AdminView = false,
                NewSearchValue = artistValues,
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders
                .GlobalWhoKnowsArtistAsync(new ContextModel(this.Context, prfx, contextUser), settings);

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

    [Command("friendwhoknows", RunMode = RunMode.Async)]
    [Summary("Shows who of your friends listen to an artist in .fmbot")]
    [Examples("fw", "fwk COMA", "friendwhoknows", "friendwhoknows DJ Seinfeld")]
    [Alias("fw", "fwk", "friendwhoknows artist", "friend whoknows", "friends whoknows", "friend whoknows artist", "friends whoknows artist")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = artistValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders
                .FriendsWhoKnowArtistAsync(new ContextModel(this.Context, prfx, contextUser),
                    currentSettings.ResponseMode, settings.NewSearchValue, settings.RedirectsEnabled);

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

    [Command("serverartists", RunMode = RunMode.Async)]
    [Summary("Top artists for your server")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`")]
    [Examples("sa", "sa a p", "serverartists", "serverartists alltime", "serverartists listeners weekly")]
    [Alias("sa", "sta", "servertopartists", "server artists", "serverartist")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists)]
    public async Task GuildArtistsAsync([Remainder] string extraOptions = null)
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
            var response = await this._artistBuilders.GuildArtistsAsync(new ContextModel(this.Context, prfx), guild, guildListSettings);

            _ = this.Interactivity.SendPaginatorAsync(
                response.StaticPaginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("affinity", RunMode = RunMode.Async)]
    [Summary("Shows users from this server with similar top artists.")]
    [Alias("n", "aff", "neighbors", "soulmates", "neighbours")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists)]
    public async Task AffinityAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);

        try
        {
            var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context, firstOptionIsLfmUsername: true);

            var largeGuild = guildUsers.Count > 2000;

            ResponseModel response;
            if (guildUsers.Count > 250)
            {
                var descriptor = userSettings.DifferentUser ? $"**{userSettings.DisplayName}**'s" : "your";

                var description = new StringBuilder();

                description.AppendLine($"<a:loading:821676038102056991> Finding {descriptor} server neighbors...");

                if (largeGuild)
                {
                    description.AppendLine();
                    description.AppendLine($"This can sometimes take a while on larger servers like this one.");
                }

                this._embed.WithDescription(description.ToString());

                var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                response = await this._artistBuilders
                    .AffinityAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, guild, guildUsers, largeGuild);

                _ = this.Interactivity.SendPaginatorAsync(
                    response.StaticPaginator,
                    message,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
            }
            else
            {
                response = await this._artistBuilders
                    .AffinityAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, guild, guildUsers, largeGuild);

                await this.Context.SendResponse(this.Interactivity, response);
            }

            this.Context.LogCommandUsed(response.CommandResponse);

        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
