using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Discord;
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
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
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

        try
        {
            var response = await this._artistBuilders.ArtistAsync(new ContextModel(this.Context, prfx, contextUser), redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

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
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = new ResponseModel();
        var artist = await this._artistsService.SearchArtist(response,
            this.Context.User,
            redirectsEnabled.NewSearchValue,
            contextUser.UserNameLastFM,
            contextUser.SessionKeyLastFm,
            userSettings.UserNameLastFm,
            true,
            userSettings.UserId,
            redirectsEnabled: redirectsEnabled.Enabled);
        if (artist.Artist == null)
        {
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
            return;
        }

        var topAlbums = await this._artistsService.GetTopAlbumsForArtist(userSettings.UserId, artist.Artist.ArtistName);
        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        if (topAlbums.Count == 0)
        {
            this._embed.WithDescription(
                $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()} has no scrobbles for this artist or their scrobbles have no album associated with them.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
            return;
        }

        var url = $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/music/{UrlEncoder.Default.Encode(artist.Artist.ArtistName)}";
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            this._embedAuthor.WithUrl(url);
        }

        var pages = new List<PageBuilder>();
        var albumPages = topAlbums.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var albumPage in albumPages)
        {
            var albumPageString = new StringBuilder();
            foreach (var artistAlbum in albumPage)
            {
                albumPageString.AppendLine($"{counter}. **{artistAlbum.Name}** - *{artistAlbum.Playcount} {StringExtensions.GetPlaysString(artistAlbum.Playcount)}*");
                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{albumPages.Count}");
            var title = new StringBuilder();

            if (userSettings.DifferentUser && userSettings.UserId != contextUser.UserId)
            {
                footer.AppendLine($" - {userSettings.UserNameLastFm} has {artist.Artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DisplayName}'s top albums for '{artist.Artist.ArtistName}'");
            }
            else
            {
                footer.Append($" - {userTitle} has {artist.Artist.UserPlaycount} total scrobbles on this artist");
                title.Append($"Your top albums for '{artist.Artist.ArtistName}'");

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            }

            this._embedAuthor.WithName(title.ToString());

            var page = new PageBuilder()
                .WithDescription(albumPageString.ToString())
                .WithAuthor(this._embedAuthor)
                .WithFooter(footer.ToString());

            pages.Add(page);
            pageCounter++;
        }

        var paginator = StringService.BuildStaticPaginator(pages);

        _ = this.Interactivity.SendPaginatorAsync(
            paginator,
            this.Context.Channel,
            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
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
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var artist = await GetArtist(redirectsEnabled.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
        if (artist == null)
        {
            return;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has " +
            $"`{artist.UserPlaycount}` {StringExtensions.GetPlaysString(artist.UserPlaycount)} for " +
            $"**{StringExtensions.Sanitize(artist.ArtistName)}**";

        if (!userSettings.DifferentUser && contextUser.LastUpdated != null)
        {
            var playsLastWeek =
                await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId, artist.ArtistName);
            if (playsLastWeek != 0)
            {
                reply += $" (`{playsLastWeek}` last week)";
            }
        }

        await this.Context.Channel.SendMessageAsync(reply, allowedMentions: AllowedMentions.None);
        this.Context.LogCommandUsed();
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
            var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.Monthly, cachedOrAllTimeOnly: true);

            if (timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                timeSettings = SettingService.GetTimePeriod("monthly", TimePeriod.Monthly);
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
        Constants.BillboardExample, Constants.ExtraLargeExample)]
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

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue, topListSettings.Discogs ? TimePeriod.AllTime : TimePeriod.Weekly, registeredLastFm: userSettings.RegisteredLastFm);

            var response = topListSettings.Discogs
                ? await this._discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings)
                : await this._artistBuilders.TopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings);

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

    [Command("taste", RunMode = RunMode.Async)]
    [Summary("Compares your top artists, genres and countries to those from another user.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionOrLfmUserNameExample, "Mode: `table` or `embed`", "XXL")]
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
            TimePeriod.AllTime);

        var tasteSettings = new TasteSettings
        {
            ExtraLarge = false
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
                WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed,
                NewSearchValue = artistValues,
                DisplayRoleFilter = false
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context,
                    prfx,
                    contextUser),
                settings.WhoKnowsMode,
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
                WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed
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
                WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed,
                NewSearchValue = artistValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders
                .FriendsWhoKnowArtistAsync(new ContextModel(this.Context, prfx, contextUser),
                    currentSettings.WhoKnowsMode, settings.NewSearchValue, settings.RedirectsEnabled);

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

    private async Task<ArtistInfo> GetArtist(string artistValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null, User user = null)
    {
        if (!string.IsNullOrWhiteSpace(artistValues) && artistValues.Length != 0)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var artistCall = await this._dataSourceFactory.GetArtistInfoAsync(artistValues, lastFmUserName);
            if (!artistCall.Success && artistCall.Error == ResponseStatus.MissingParameters)
            {
                this._embed.WithDescription($"Artist `{artistValues}` could not be found, please check your search values and try again.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }
            if (!artistCall.Success || artistCall.Content == null)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context.Message.Content, this.Context.User, "artist");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                return null;
            }

            return artistCall.Content;
        }
        else
        {
            Response<RecentTrackList> recentScrobbles;

            if (user != null)
            {
                recentScrobbles = await this._updateService.UpdateUserAndGetRecentTracks(user);
            }
            else
            {
                recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
            {
                return null;
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            var artistCall = await this._dataSourceFactory.GetArtistInfoAsync(lastPlayedTrack.ArtistName, lastFmUserName);

            if (artistCall.Content == null || !artistCall.Success)
            {
                this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.ArtistName}**.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return artistCall.Content;
        }
    }
}
