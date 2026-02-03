using System;
using System.Threading.Tasks;
using CsvHelper.Configuration.Attributes;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Constants = FMBot.Domain.Constants;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Albums")]
public class AlbumCommands(
    GuildService guildService,
    IndexService indexService,
    IPrefixService prefixService,
    SettingService settingService,
    UserService userService,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings,
    AlbumBuilders albumBuilders,
    PlayBuilder playBuilder)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("album", "ab")]
    [Summary("Shows album you're currently listening to or searching for.")]
    [Examples(
        "ab",
        "album",
        "album Ventura Anderson .Paak",
        "ab Boy Harsher | Yr Body Is Nothing")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    [SupporterEnhanced("Supporters can see the date they first discovered an album")]
    public async Task AlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response =
                await albumBuilders.AlbumAsync(new ContextModel(this.Context, prfx, contextUser), albumValues);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("albumplays", "abp", "albumplay", "abplays", "albump")]
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
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumPlaysAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(albumValues, contextUser, this.Context);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await albumBuilders.AlbumPlaysAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("cover", "abc", "abco", "co", "albumcover")]
    [Summary("Cover for current album or the one you're searching for.")]
    [Examples(
        "co",
        "cover",
        "cover la priest inji")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Charts)]
    public async Task AlbumCoverAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await settingService.GetUser(albumValues, contextUser, this.Context);

        try
        {
            var response = await albumBuilders.CoverAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, albumValues);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("topalbums", "abl", "abs", "tab", "albumlist", "albums", "albumslist", "talbum")]
    [Summary("Shows your or someone else's top albums over a certain time period.")]
    [Options(Constants.CompactTimePeriodList,
        "Albums released in year: `r:2023`, `released:2023`",
        "Albums released in decade: `d:80s`, `decade:1990`",
        Constants.UserMentionExample, Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("tab", "topalbums", "tab a lfm:fm-bot", "topalbums weekly @user", "tab bb xl")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    public async Task TopAlbumsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue,
                topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue
                    ? TimePeriod.AllTime
                    : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var mode = SettingService.SetMode(timeSettings.NewSearchValue, contextUser.Mode);

            var response = await albumBuilders.TopAlbumsAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("whoknowsalbum", "wa", "wka", "wkab", "wab", "wkab", "wkal", "wkalbum")]
    [Summary("Shows what other users listen to an album in your server")]
    [Examples("wa", "whoknowsalbum", "whoknowsalbum the beatles abbey road",
        "whoknowsalbum Metallica & Lou Reed | Lulu")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task WhoKnowsAlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
                NewSearchValue = albumValues,
                DisplayRoleFilter = false
            };

            var settings = SettingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await albumBuilders.WhoKnowsAlbumAsync(
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

    [Command("globalwhoknowsalbum", "gwa", "gwka", "gwab", "gwkab", "globalwka", "globalwkalbum")]
    [Summary("Shows what other users listen to the an album on .fmbot")]
    [Examples("gwa", "globalwhoknowsalbum", "globalwhoknowsalbum the beatles abbey road",
        "globalwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsAlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = albumValues
        };

        var settings = SettingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType, true);

        try
        {
            var response = await albumBuilders.GlobalWhoKnowsAlbumAsync(
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
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                    { Content = "Error while replying: The bot is missing permissions.\nMake sure it has permission to 'Embed links' and 'Attach Images'" });
            }
            else
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
    }

    [Command("friendwhoknowsalbum", "fwa", "fwka", "fwkab", "fwab")]
    [Summary("Who of your friends listen to an album")]
    [Examples("fwa", "fwka COMA", "friendwhoknows", "friendwhoknowsalbum the beatles abbey road",
        "friendwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.WhoKnowsMode ?? WhoKnowsResponseMode.Default,
                NewSearchValue = albumValues
            };

            var settings = SettingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await albumBuilders
                .FriendsWhoKnowAlbumAsync(new ContextModel(this.Context, prfx, contextUser),
                    settings.ResponseMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("Response status code does not indicate success: 403"))
            {
                await this.Context.HandleCommandException(e, userService, sendReply: false);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                    { Content = "Error while replying: The bot is missing permissions.\nMake sure it has permission to 'Embed links' and 'Attach Images'" });
            }
            else
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
    }

    [Command("albumtracks", "abt", "abtracks", "albumt")]
    [Summary("Shows track playcounts for a specific album")]
    [Examples("abt", "albumtracks", "albumtracks de jeugd van tegenwoordig machine",
        "albumtracks This Old Dog plays", "albumtracks U2 | The Joshua Tree")]
    [Options("Order by plays: `plays`")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumTracksAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        try
        {
            var orderByPlaycount = SettingService.OrderByPlaycount(albumValues);
            var userSettings = await settingService.GetUser(orderByPlaycount.NewSearchValue, contextUser, this.Context);

            var response = await albumBuilders.AlbumTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, userSettings.NewSearchValue, orderByPlaycount.Enabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("serveralbums", "sab", "stab", "servertopalbums", "serveralbum")]
    [Summary("Top albums for your server, optionally for a specific artist.")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`",
        "Artist name")]
    [Examples("sab", "sab a p", "serveralbums", "serveralbums alltime", "serveralbums listeners weekly",
        "serveralbums the beatles monthly")]
    [RequiresIndex]
    [GuildOnly]
    [CommandCategories(CommandCategory.Albums)]
    public async Task GuildAlbumsAsync([CommandParameter(Remainder = true)] string guildAlbumsOptions = null)
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
                await albumBuilders.GuildAlbumsAsync(new ContextModel(this.Context, prfx), guild,
                    guildListSettings);

            _ = this.Interactivity.SendPaginatorAsync(
                response.ComponentPaginator.Build(),
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("albumgaps", "agaps", "agap", "abgaps", "abgap", "albumgap")]
    [Summary("Shows the albums you've returned to after a gap in listening.")]
    [Options(Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("agaps", "albumgaps", "albumgaps @user")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    [SupporterExclusive(
        "To see which albums you've re-discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task AlbumGapsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
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

            var response = await playBuilder.ListeningGapsAsync(context, topListSettings, userSettings,
                mode.mode, GapEntityType.Album);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
