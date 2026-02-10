using System;
using System.Globalization;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using TimePeriod = FMBot.Domain.Models.TimePeriod;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Tracks")]
public class TrackCommands(
    GuildService guildService,
    IndexService indexService,
    IPrefixService prefixService,
    IDataSourceFactory dataSourceFactory,
    SettingService settingService,
    UserService userService,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings,
    TrackService trackService,
    TrackBuilders trackBuilders,
    EurovisionBuilders eurovisionBuilders,
    PlayBuilder playBuilders,
    CountryService countryService)
    : BaseCommandModule(botSettings)
{
    private readonly IDataSourceFactory _dataSourceFactory = dataSourceFactory;
    private readonly TrackService _trackService = trackService;

    private InteractiveService Interactivity { get; } = interactivity;


    [Command("track", "tr", "ti", "ts", "trackinfo")]
    [Summary("Track you're currently listening to or searching for.")]
    [Examples(
        "tr",
        "track",
        "track Depeche Mode Enjoy The Silence",
        "track Crystal Waters | Gypsy Woman (She's Homeless) - Radio Edit")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterEnhanced("Supporters can see the date they first discovered a track")]
    public async Task TrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response =
                await trackBuilders.TrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("trackplays", "tp", "trackplay", "tplays", "trackp")]
    [Summary("Shows playcount for current track or the one you're searching for.\n\n" +
             "You can also mention another user to see their playcount.")]
    [Examples(
        "tp",
        "trackplays",
        "trackplays Mac DeMarco Here Comes The Cowboy",
        "tp lfm:fm-bot",
        "trackplays Cocteau Twins | Heaven or Las Vegas @user")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TrackPlaysAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(trackValues, contextUser, this.Context);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await trackBuilders.TrackPlays(new ContextModel(this.Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("trackdetails", "td", "trackdata", "trackmetadata", "tds")]
    [Summary("Shows metadata for current track or the one you're searching for.")]
    [Examples(
        "tp",
        "trackdetails",
        "td Mac DeMarco Here Comes The Cowboy")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TrackDetailsAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await trackBuilders.TrackDetails(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("love", "l", "heart", "favorite", "affection", "appreciation", "lust", "fuckyeah", "fukk", "unfuck")]
    [Summary("Loves a track on Last.fm")]
    [Examples("love", "l", "love Tame Impala Borderline")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LoveAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await trackBuilders.LoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("unlove", "ul", "unheart", "hate", "fuck")]
    [Summary("Removes the track you're currently listening to or searching for from your last.fm loved tracks.")]
    [Examples("unlove", "ul", "unlove Lou Reed Brandenburg Gate")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task UnLoveAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("loved", "lovedtracks", "lt", "unfucked")]
    [Summary("Shows your Last.fm loved tracks.")]
    [Examples("loved", "lt", "lovedtracks lfm:fm-bot", "lovedtracks @user")]
    [UserSessionRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LovedAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);

        try
        {
            var response = await trackBuilders.LovedTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("scrobble", "sb")]
    [Summary("Scrobbles a track on Last.fm.")]
    [Examples("scrobble", "sb the less i know the better", "scrobble Loona Heart Attack",
        "scrobble Mac DeMarco | Chamber of Reflection")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task ScrobbleAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var response = await trackBuilders.ScrobbleAsync(new ContextModel(this.Context, prfx, contextUser),
            trackValues);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("toptracks", "tt", "tl", "tracklist", "tracks", "trackslist", "ttracks")]
    [Summary("Shows your or someone else's top tracks over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("tt", "toptracks", "tt y", "toptracks weekly @user", "tt bb xl")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TopTracksAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await trackBuilders.TopTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("receipt", "rcpt", "receiptify", "reciept")]
    [Summary("Shows your track receipt. Based on Receiptify.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("receipt", "receipt 2022", "rcpt week")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task ReceiptAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            if (timeSettings.DefaultPicked)
            {
                var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
                timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm,
                    timeZone: userSettings.TimeZone);
            }

            var response = await trackBuilders.GetReceipt(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("whoknowstrack", "wt", "wkt", "wktr", "wtr", "wktrack")]
    [Summary("Shows what other users listen to a track in your server")]
    [Examples("wt", "whoknowstrack", "whoknowstrack Hothouse Flowers Don't Go",
        "whoknowstrack Natasha Bedingfield | Unwritten")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task WhoKnowsTrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
                NewSearchValue = trackValues,
                DisplayRoleFilter = false
            };

            var settings = SettingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

            var response = await trackBuilders.WhoKnowsTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("globalwhoknowstrack", "gwt", "gwkt", "gwtr", "gwktr", "globalwkt", "globalwktrack")]
    [Summary("Shows what other users listen to a track in .fmbot")]
    [Examples("gwt", "globalwhoknowstrack", "globalwhoknowstrack Hothouse Flowers Don't Go",
        "globalwhoknowstrack Natasha Bedingfield | Unwritten")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsTrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = trackValues
        };
        var settings = SettingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType, true);

        try
        {
            var response = await trackBuilders.GlobalWhoKnowsTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("Response status code does not indicate success: 403"))
            {
                await this.Context.HandleCommandException(e, userService, sendReply: false);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error while replying: The bot is missing permissions.\nMake sure it has permission to 'Embed links' and 'Attach Images'" });
            }
            else
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
    }

    [Command("friendwhoknowstrack", "fwt", "fwkt", "fwktr", "fwtrack")]
    [Summary("Shows who of your friends listen to a track in .fmbot")]
    [Examples("fwt", "fwkt The Beatles Yesterday", "friendwhoknowstrack",
        "friendwhoknowstrack Hothouse Flowers Don't Go", "friendwhoknowstrack Mall Grab | Sunflower")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsTrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserWithFriendsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
            HidePrivateUsers = false,
            NewSearchValue = trackValues
        };
        var settings = SettingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

        try
        {
            var response = await trackBuilders.FriendsWhoKnowTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("Response status code does not indicate success: 403"))
            {
                await this.Context.HandleCommandException(e, userService, sendReply: false);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error while replying: The bot is missing permissions.\nMake sure it has permission to 'Embed links' and 'Attach Images'" });
            }
            else
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
    }

    [Command("servertracks", "st", "stt", "servertoptracks", "servertrack", "billboard", "bb")]
    [Summary("Top tracks for your server, optionally for an artist")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`",
        "Artist name")]
    [Examples("st", "st a p", "servertracks", "servertracks alltime", "servertracks listeners weekly",
        "servertracks the beatles listeners", "servertracks the beatles alltime")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task GuildTracksAsync([CommandParameter(Remainder = true)] string guildTracksOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = guildTracksOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, guildTracksOptions);
        var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue,
            guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response =
                await trackBuilders.GuildTracksAsync(new ContextModel(this.Context, prfx), guild,
                    guildListSettings);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("eurovision", "ev", "esc", "eurovisie", "eurovisionsongcontest", "songcontest")]
    public async Task EurovisionAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, prfx, contextUser);

        try
        {
            CountryInfo pickedCountry = null;
            if (extraOptions != null)
            {
                var splitOptions = extraOptions.Split(" ");
                foreach (var option in splitOptions)
                {
                    pickedCountry = countryService.GetValidCountry(option);
                    if (pickedCountry != null)
                    {
                        break;
                    }
                }
            }

            var year = SettingService.GetYear(extraOptions);

            ResponseModel response;
            if (pickedCountry != null)
            {
                response = await eurovisionBuilders.GetEurovisionCountryYear(context, pickedCountry, year ?? DateTime.UtcNow.Year);
            }
            else
            {
                response =
                    await eurovisionBuilders.GetEurovisionYear(context, year ?? DateTime.UtcNow.Year);
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("trackgaps", "tgaps", "tgap", "trackgap", "songgaps", "songgap")]
    [Summary("Shows the tracks you've returned to after a gap in listening.")]
    [Options(Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("tgaps", "trackgaps", "trackgaps @user")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterExclusive(
        "To see which tracks you've re-discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task TrackGapsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);

            var supporterRequiredResponse = PlayBuilder.GapsSupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, userService);
                await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
                return;
            }

            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var mode = SettingService.SetMode(userSettings.NewSearchValue, contextUser.Mode);

            var response = await playBuilders.ListeningGapsAsync(context, topListSettings, userSettings,
                mode.mode, GapEntityType.Track);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("lyrics", "lyric", "lyr", "lr", "lyricsfind", "lyricsearch", "lyricssearch")]
    [Summary("Lyrics for a track you're currently listening to or searching for")]
    [Examples(
        "lyrics",
        "l",
        "lyrics The Beatles Let It Be",
        "lyrics Daft Punk | Get Lucky")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterExclusive("Viewing track lyrics in .fmbot is only available for .fmbot supporters")]
    public async Task LyricsAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var context = new ContextModel(this.Context, prfx, contextUser);

            var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, userService);
                await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
                return;
            }

            var response = await trackBuilders.TrackLyricsAsync(context, trackValues);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
