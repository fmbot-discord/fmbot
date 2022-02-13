using System;
using System.Collections.Generic;
using System.Linq;
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
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.Commands.LastFM;

[Name("Artists")]
public class ArtistCommands : BaseCommandModule
{
    private readonly ArtistBuilders _artistBuilders;
    private readonly ArtistsService _artistsService;
    private readonly CrownService _crownService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;

    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly SettingService _settingService;
    private readonly SpotifyService _spotifyService;
    private readonly UserService _userService;
    private readonly GenreService _genreService;
    private readonly FriendsService _friendsService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly WhoKnowsArtistService _whoKnowArtistService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly SmallIndexRepository _smallIndexRepository;

    private InteractiveService Interactivity { get; }

    public ArtistCommands(
        ArtistsService artistsService,
        CrownService crownService,
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        SpotifyService spotifyService,
        UserService userService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsPlayService whoKnowsPlayService,
        InteractiveService interactivity,
        WhoKnowsService whoKnowsService,
        IOptions<BotSettings> botSettings,
        GenreService genreService,
        FriendsService friendsService,
        ArtistBuilders artistBuilders, SmallIndexRepository smallIndexRepository) : base(botSettings)
    {
        this._artistsService = artistsService;
        this._crownService = crownService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._spotifyService = spotifyService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowArtistService = whoKnowsArtistService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this.Interactivity = interactivity;
        this._whoKnowsService = whoKnowsService;
        this._genreService = genreService;
        this._friendsService = friendsService;
        this._artistBuilders = artistBuilders;
        this._smallIndexRepository = smallIndexRepository;
    }

    [Command("artist", RunMode = RunMode.Async)]
    [Summary("Artist you're currently listening to or searching for.")]
    [Examples(
        "a",
        "artist",
        "a Gorillaz",
        "artist Gamma Intel")]
    [Alias("a")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistAsync([Remainder] string artistValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._artistBuilders.ArtistAsync(new ContextModel(this.Context, prfx, contextUser), artistValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
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

        var timeSettings = SettingService.GetTimePeriod(userSettings.NewSearchValue, TimePeriod.AllTime, cachedOrAllTimeOnly: true, dailyTimePeriods: false);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, prfx, contextUser), timeSettings,
            userSettings, userSettings.NewSearchValue);

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

        var response = new ResponseModel();
        var artist = await this._artistsService.GetArtist(response,
            this.Context.User,
            userSettings.NewSearchValue,
            contextUser.UserNameLastFM,
            contextUser.SessionKeyLastFm,
            userSettings.UserNameLastFm,
            true,
            userSettings.UserId);
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
                $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()} has no scrobbles for this artist or their scrobbles have no album associated with them.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
            return;
        }

        var url = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/music/{UrlEncoder.Default.Encode(artist.Artist.ArtistName)}";
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
                albumPageString.AppendLine($"{counter}. **{artistAlbum.Name}** ({artistAlbum.Playcount} {StringExtensions.GetPlaysString(artistAlbum.Playcount)})");
                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{albumPages.Count}");
            var title = new StringBuilder();

            if (userSettings.DifferentUser && userSettings.UserId != contextUser.UserId)
            {
                footer.AppendLine($" - {userSettings.UserNameLastFm} has {artist.Artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DiscordUserName} their top albums for '{artist.Artist.ArtistName}'");
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

        var artist = await GetArtist(userSettings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
        if (artist == null)
        {
            return;
        }

        var reply =
            $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has " +
            $"`{artist.UserPlaycount}` {StringExtensions.GetPlaysString(artist.UserPlaycount)} for " +
            $"**{artist.ArtistName.FilterOutMentions()}**";

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
    [Alias("apc")]
    [CommandCategories(CommandCategory.Other)]
    public async Task ArtistPaceAsync([Remainder] string extraOptions = null)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var timeSettings = SettingService.GetTimePeriod(userSettings.NewSearchValue, TimePeriod.Monthly, cachedOrAllTimeOnly: true);

            if (timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                timeSettings = SettingService.GetTimePeriod("monthly", TimePeriod.Monthly);
            }

            long timeFrom;
            if (timeSettings.TimePeriod != TimePeriod.AllTime && timeSettings.PlayDays != null)
            {
                var dateAgo = DateTime.UtcNow.AddDays(-timeSettings.PlayDays.Value);
                timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
            }
            else
            {
                timeFrom = userInfo.Registered.Unixtime;
            }

            var response = await this._artistBuilders.ArtistPaceAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, timeSettings.NewSearchValue, timeFrom, null);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Unable to show artist pace due to an internal error.");
        }
    }

    [Command("topartists", RunMode = RunMode.Async)]
    [Summary("Shows your or someone else their top artists over a certain time period.")]
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
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);

            var pages = new List<PageBuilder>();

            string userTitle;
            if (!userSettings.DifferentUser)
            {
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                userTitle = await this._userService.GetUserTitleAsync(this.Context);
            }
            else
            {
                userTitle =
                    $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
            }

            var userUrl =
                $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}";

            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artists for {userTitle}");
            this._embedAuthor.WithUrl(userUrl);

            var artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 200, 1);

            if (!artists.Success || artists.Content == null)
            {
                this._embed.ErrorResponse(artists.Error, artists.Message, this.Context.Message.Content, this.Context.User);
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }
            if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
            {
                this._embed.WithDescription($"Sorry, you or the user you're searching for don't have any top artists in the [selected time period]({userUrl}).");
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                this._embed.WithColor(DiscordConstants.WarningColorOrange);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var previousTopArtists = new List<TopArtist>();
            if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
            {
                var previousArtistsCall = await this._lastFmRepository
                    .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

                if (previousArtistsCall.Success)
                {
                    previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
                }
            }

            var artistPages = artists.Content.TopArtists
                .ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

            var counter = 1;
            var pageCounter = 1;
            var rnd = new Random().Next(0, 4);

            foreach (var artistPage in artistPages)
            {
                var artistPageString = new StringBuilder();
                foreach (var artist in artistPage)
                {
                    var name =
                        $"**[{artist.ArtistName}]({artist.ArtistUrl})** ({artist.UserPlaycount} {StringExtensions.GetPlaysString(artist.UserPlaycount)})";

                    if (topListSettings.Billboard && previousTopArtists.Any())
                    {
                        var previousTopArtist = previousTopArtists.FirstOrDefault(f => f.ArtistName == artist.ArtistName);
                        int? previousPosition = previousTopArtist == null ? null : previousTopArtists.IndexOf(previousTopArtist);

                        artistPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                    }
                    else
                    {
                        artistPageString.Append($"{counter}. ");
                        artistPageString.AppendLine(name);
                    }

                    counter++;
                }

                var footer = new StringBuilder();
                footer.Append($"Page {pageCounter}/{artistPages.Count}");

                if (artists.Content.TotalAmount.HasValue)
                {
                    footer.Append($" - { artists.Content.TotalAmount} different artists in this time period");
                }
                if (topListSettings.Billboard)
                {
                    footer.AppendLine();
                    footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
                }

                if (rnd == 1 && !topListSettings.Billboard)
                {
                    footer.AppendLine();
                    footer.Append("View this list as a billboard by adding 'billboard' or 'bb'");
                }

                pages.Add(new PageBuilder()
                    .WithDescription(artistPageString.ToString())
                    .WithAuthor(this._embedAuthor)
                    .WithFooter(footer.ToString()));
                pageCounter++;
            }

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed();

            if (!userSettings.DifferentUser && timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                await this._smallIndexRepository.UpdateUserArtists(contextUser, artists.Content.TopArtists);
            }
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Unable to show Last.fm info due to an internal error.");
        }
    }

    [Command("taste", RunMode = RunMode.Async)]
    [Summary("Compares your top artists to another users top artists.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionOrLfmUserNameExample, "Mode: `table` or `embed`")]
    [Examples("t frikandel_", "t @user", "taste bitldev", "taste @user monthly embed")]
    [UsernameSetRequired]
    [Alias("t")]
    [CommandCategories(CommandCategory.Artists)]
    public async Task TasteAsync(string user = null, [Remainder] string extraOptions = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (user == "help")
        {
            await ReplyAsync(
                $"Usage: `{prfx}taste 'last.fm username/ discord mention' '{Constants.CompactTimePeriodList}' 'table/embed'`");
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var timeSettings = SettingService.GetTimePeriod(
            extraOptions,
            TimePeriod.AllTime);

        var tasteSettings = new TasteSettings
        {
            ChartTimePeriod = timeSettings.TimePeriod
        };

        tasteSettings = this._artistsService.SetTasteSettings(tasteSettings, extraOptions);

        try
        {
            var ownLastFmUsername = userSettings.UserNameLastFM;
            string lastfmToCompare = null;

            if (user != null)
            {
                string alternativeLastFmUserName;

                if (await this._lastFmRepository.LastFmUserExistsAsync(user))
                {
                    alternativeLastFmUserName = user;
                }
                else
                {
                    var otherUser = await this._settingService.StringWithDiscordIdForUser(user);

                    alternativeLastFmUserName = otherUser?.UserNameLastFM;
                }

                if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                {
                    lastfmToCompare = alternativeLastFmUserName;
                }
            }

            if (lastfmToCompare == null)
            {
                this._embed.WithDescription($"Please enter a Last.fm username or mention someone to compare yourself to.\n" +
                                            $"Examples:\n" +
                                            $"- `{prfx}taste fm-bot`\n" +
                                            $"- `{prfx}taste @.fmbot`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            if (lastfmToCompare.ToLower() == userSettings.UserNameLastFM.ToLower())
            {
                this._embed.WithDescription($"You can't compare your own taste with yourself. For viewing your top artists, use `{prfx}topartists`.\n\n" +
                                            $"Please enter a Last.fm username or mention someone to compare yourself to.\n" +
                                            $"Examples:\n" +
                                            $"- `{prfx}taste fm-bot`\n" +
                                            $"- `{prfx}taste @.fmbot`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            tasteSettings.OtherUserLastFmUsername = lastfmToCompare;

            var ownArtistsTask = this._lastFmRepository.GetTopArtistsAsync(ownLastFmUsername, timeSettings, 1000);
            var otherArtistsTask = this._lastFmRepository.GetTopArtistsAsync(lastfmToCompare, timeSettings, 1000);

            var ownArtists = await ownArtistsTask;
            var otherArtists = await otherArtistsTask;


            if (!ownArtists.Success || ownArtists.Content == null || !otherArtists.Success || otherArtists.Content == null)
            {
                this._embed.ErrorResponse(ownArtists.Error, ownArtists.Message, this.Context.Message.Content, this.Context.User);
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            if (ownArtists.Content.TopArtists == null || ownArtists.Content.TopArtists.Count == 0 || otherArtists.Content.TopArtists == null || otherArtists.Content.TopArtists.Count == 0)
            {
                await ReplyAsync(
                    $"Sorry, you or the other user don't have any artist plays in the selected time period.");
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                return;
            }

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Top artist comparison - {ownLastFmUsername} vs {lastfmToCompare}");
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{lastfmToCompare}/library/artists?{timeSettings.UrlParameter}");
            this._embed.WithAuthor(this._embedAuthor);

            int amount = 14;
            if (tasteSettings.TasteType == TasteType.FullEmbed)
            {
                var taste = this._artistsService.GetEmbedTaste(ownArtists.Content, otherArtists.Content, amount, timeSettings.TimePeriod);

                this._embed.WithDescription(taste.Description);
                this._embed.AddField("Artist", taste.LeftDescription, true);
                this._embed.AddField("Plays", taste.RightDescription, true);
            }
            else
            {
                var taste = this._artistsService.GetTableTaste(ownArtists.Content, otherArtists.Content, amount, timeSettings.TimePeriod, ownLastFmUsername, lastfmToCompare);

                this._embed.WithDescription(taste);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Unable to show Last.fm info due to an internal error.");
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
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, prfx, contextUser),
                guild, artistValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows.");
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
            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                HidePrivateUsers = false,
                ShowBotters = false,
                AdminView = false,
                NewSearchValue = artistValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await this._artistBuilders
                .GlobalWhoKnowsArtistAsync(new ContextModel(this.Context, prfx, contextUser), guild, settings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using global whoknows.");
            }
        }
    }

    [Command("friendwhoknows", RunMode = RunMode.Async)]
    [Summary("Shows who of your friends listen to an artist in .fmbot")]
    [Examples("fw", "fwk COMA", "friendwhoknows", "friendwhoknows DJ Seinfeld")]
    [Alias("fw", "fwk", "friendwhoknows artist", "friend whoknows", "friends whoknows", "friend whoknows artist", "friends whoknows artist")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAsync([Remainder] string artistValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var user = await this._userService.GetUserWithFriendsAsync(this.Context.User);

            if (user.Friends?.Any() != true)
            {
                await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                 $"`{prfx}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            string artistName;
            string artistUrl;
            string spotifyImageUrl;
            long? userPlaycount;

            var cachedArtist = await this._artistsService.GetArtistFromDatabase(artistValues);

            if (user.LastUpdated > DateTime.UtcNow.AddHours(-1) && cachedArtist != null)
            {
                artistName = cachedArtist.Name;
                artistUrl = cachedArtist.LastFmUrl;
                spotifyImageUrl = cachedArtist.SpotifyImageUrl;

                userPlaycount = await this._whoKnowArtistService.GetArtistPlayCountForUser(artistName, user.UserId);
            }
            else
            {
                var artist = await GetArtist(artistValues, user.UserNameLastFM, user.SessionKeyLastFm);
                if (artist == null)
                {
                    return;
                }

                artistName = artist.ArtistName;
                artistUrl = artist.ArtistUrl;

                cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artist, artist.ArtistName);
                spotifyImageUrl = cachedArtist.SpotifyImageUrl;
                userPlaycount = artist.UserPlaycount;
                if (userPlaycount.HasValue)
                {
                    await this._updateService.CorrectUserArtistPlaycount(user.UserId, artist.ArtistName,
                        userPlaycount.Value);
                }
            }

            var usersWithArtist = await this._whoKnowArtistService.GetFriendUsersForArtists(this.Context, guild.GuildId, user.UserId, artistName);

            if (userPlaycount != 0 && this.Context.Guild != null)
            {
                var discordGuildUser = await this.Context.Guild.GetUserAsync(user.DiscordUserId);
                var guildUser = new GuildUser
                {
                    UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : user.UserNameLastFM,
                    User = user
                };
                usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, artistName, userPlaycount);
            }

            var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithArtist, user.UserId, PrivacyLevel.Server);
            if (usersWithArtist.Count == 0)
            {
                serverUsers = "None of your friends has listened to this artist.";
            }

            this._embed.WithDescription(serverUsers);

            var footer = "";

            if (cachedArtist.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
            {
                footer += $"\n{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}";
            }

            var amountOfHiddenFriends = user.Friends.Count(c => !c.FriendUserId.HasValue);
            if (amountOfHiddenFriends > 0)
            {
                footer += $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            footer += $"\nFriends WhoKnow artist requested by {userTitle}";

            if (usersWithArtist.Any() && usersWithArtist.Count > 1)
            {
                var globalListeners = usersWithArtist.Count;
                var globalPlaycount = usersWithArtist.Sum(a => a.Playcount);
                var avgPlaycount = usersWithArtist.Average(a => a.Playcount);

                footer += $"\n{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ";
                footer += $"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ";
                footer += $"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}";
            }

            this._embed.WithTitle($"{artistName} with friends");

            if (Uri.IsWellFormedUriString(artistUrl, UriKind.Absolute))
            {
                this._embed.WithUrl(artistUrl);
            }

            this._embedFooter.WithText(footer);
            this._embed.WithFooter(this._embedFooter);

            if (spotifyImageUrl != null)
            {
                this._embed.WithThumbnailUrl(spotifyImageUrl);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using friend whoknows.");
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
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Something went wrong while using serverartists. Please report this issue.");
        }
    }

    [Command("affinity", RunMode = RunMode.Async)]
    [Summary("Shows server users with similar top artists.\n\n" +
             "This command is still a work in progress.")]
    [Alias("n", "aff", "neighbors")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists)]
    public async Task AffinityAsync()
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
        var filteredGuildUsers = GuildService.FilterGuildUsersAsync(guild);

        _ = this.Context.Channel.TriggerTypingAsync();

        var users = filteredGuildUsers.Select(s => s.User).ToList();
        var neighbors = await this._whoKnowArtistService.GetNeighbors(users, userSettings.UserId);

        var description = new StringBuilder();

        foreach (var neighbor in neighbors.Take(15))
        {
            description.AppendLine($"**[{neighbor.Name}]({Constants.LastFMUserUrl}{neighbor.LastFMUsername})** " +
                                   $"- {neighbor.MatchPercentage:0.0}%");
        }

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        this._embed.WithTitle($"Neighbors for {userTitle}");
        this._embed.WithFooter("Experimental command - work in progress");

        this._embed.WithDescription(description.ToString());

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

    }

    private async Task<ArtistInfo> GetArtist(string artistValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null, User user = null)
    {
        if (!string.IsNullOrWhiteSpace(artistValues) && artistValues.Length != 0)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistValues, lastFmUserName);
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
                recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
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

            var artistCall = await this._lastFmRepository.GetArtistInfoAsync(lastPlayedTrack.ArtistName, lastFmUserName);

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
