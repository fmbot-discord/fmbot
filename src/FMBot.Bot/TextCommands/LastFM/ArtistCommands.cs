using System;
using System.Text;
using System.Threading.Tasks;
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
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Services.Commands;
using TimePeriod = FMBot.Domain.Models.TimePeriod;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Artists")]
public class ArtistCommands(
    ArtistsService artistsService,
    GuildService guildService,
    IndexService indexService,
    IPrefixService prefixService,
    SettingService settingService,
    UserService userService,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings,
    ArtistBuilders artistBuilders,
    DiscogsBuilder discogsBuilders,
    PlayBuilder playBuilders)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("artist", "a", "ai", "artistinfo")]
    [Summary("Artist you're currently listening to or searching for.")]
    [Examples(
        "a",
        "artist",
        "a Gorillaz",
        "artist Gamma Intel")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    [SupporterEnhanced("Supporters can see the date they first discovered an artist")]
    public async Task ArtistAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(artistValues);

        var userSettings = await settingService.GetUser(artistValues, contextUser, this.Context);

        try
        {
            var response = await artistBuilders.ArtistInfoAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("artistoverview", "ao", "artistsoverview")]
    [Summary("Artist you're currently listening to or searching for.")]
    [Examples(
        "ao",
        "artistoverview",
        "ao Gorillaz",
        "artistoverview Gamma Intel")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistOverviewAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
        var userSettings = await settingService.GetUser(artistValues, contextUser, this.Context);

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        try
        {
            var response = await artistBuilders.ArtistOverviewAsync(
                new ContextModel(this.Context, prfx, contextUser),
                userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("artisttracks", "at", "att", "artisttrack", "artistrack", "artisttoptracks",
        "artisttoptrack", "favs")]
    [Summary("Top tracks for an artist")]
    [Examples(
        "at",
        "artisttracks",
        "artisttracks DMX")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    [SupporterEnhanced("Supporters have their complete Last.fm history cached in the bot, so the artisttracks command always contains all their tracks")]
    public async Task ArtistTracksAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);
        var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.AllTime,
            cachedOrAllTimeOnly: true, dailyTimePeriods: false);

        var response = await artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, prfx, contextUser),
            timeSettings,
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("artistalbums", "aa", "aab", "atab", "artistalbum", "artistopalbum", "artisttopalbums",
        "artisttab")]
    [Summary("Top albums for an artist.")]
    [Examples(
        "aa",
        "artistalbums",
        "artistalbums The Prodigy")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    [SupporterEnhanced("Supporters have their complete Last.fm history cached in the bot, so the artistalbums command always contains all their albums")]
    public async Task ArtistAlbumsAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await artistBuilders.ArtistAlbumsAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("artistplays", "ap")]
    [Summary("Shows playcount for current artist or the one you're searching for.\n\n" +
             "You can also mention another user to see their playcount.")]
    [Examples(
        "ap",
        "artistplays",
        "albumplays @user",
        "ap lfm:fm-bot",
        "artistplays Mall Grab @user")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistPlaysAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var userSettings = await settingService.GetUser(artistValues, contextUser, this.Context);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await artistBuilders.ArtistPlaysAsync(new ContextModel(this.Context, prfx, contextUser),
            userSettings,
            redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("artistpace", "apc", "apace", "artistpc")]
    [Summary("Shows estimated date you reach a certain amount of plays on an artist")]
    [Options("weekly/monthly", "Optional goal amount: For example `500` or `2k`", Constants.UserMentionExample)]
    [Examples("apc", "apc 1k q", "apc 400 h @user", "artistpace", "artistpace weekly @user 2500")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task ArtistPaceAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);
            var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.Monthly,
                cachedOrAllTimeOnly: true, timeZone: userSettings.TimeZone);

            if (timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                timeSettings =
                    SettingService.GetTimePeriod("monthly", TimePeriod.Monthly, timeZone: userSettings.TimeZone);
            }

            var response = await artistBuilders.ArtistPaceAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, timeSettings.NewSearchValue, null, redirectsEnabled.Enabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("topartists", "al", "as", "ta", "artistlist", "artists", "artistslist")]
    [Summary("Shows your or someone else's top artists over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("ta", "topartists", "ta a lfm:fm-bot", "topartists weekly @user", "ta bb xl")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Artists)]
    public async Task TopArtistsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(userSettings.NewSearchValue);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue,
                topListSettings.Discogs ? TimePeriod.AllTime : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = topListSettings.Discogs
                ? await discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings)
                : await artistBuilders.TopArtistsAsync(new ContextModel(this.Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);

            //if (!userSettings.DifferentUser && timeSettings.TimePeriod == TimePeriod.AllTime)
            //{
            //    await this._smallIndexRepository.UpdateUserArtists(contextUser, artists.Content.TopArtists);
            //}
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("discoveries", "d", "discovered", "discovery", "artistdiscoveries", "firstlistened")]
    [Summary("Artists you've recently discovered")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("d", "discovered", "ta a lfm:fm-bot", "topartists weekly @user", "ta bb xl")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Artists)]
    [SupporterExclusive(
        "To see what music you've recently discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task ArtistDiscoveriesAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);

            var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, userService);
                await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
                return;
            }

            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue, TimePeriod.Quarterly,
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(timeSettings.NewSearchValue, contextUser.Mode);

            var response = await artistBuilders.ArtistDiscoveriesAsync(context, topListSettings, timeSettings,
                userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("taste", "t")]
    [Summary("Compares your top artists, genres and countries to those from another user.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionOrLfmUserNameExample, "Mode: `table` or `embed`",
        Constants.EmbedSizeExample)]
    [Examples("t frikandel_", "t @user", "taste bitldev", "taste @user monthly embed")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task TasteAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var otherUser =
            await settingService.GetUser(extraOptions, userSettings, this.Context,
                firstOptionIsLfmUsername: true);

        var timeSettings = SettingService.GetTimePeriod(
            otherUser.NewSearchValue,
            TimePeriod.AllTime,
            timeZone: userSettings.TimeZone);

        var tasteSettings = new TasteSettings
        {
            EmbedSize = EmbedSize.Default
        };

        tasteSettings = artistsService.SetTasteSettings(tasteSettings, timeSettings.NewSearchValue);

        try
        {
            var response = await artistBuilders.TasteAsync(new ContextModel(this.Context, prfx, userSettings),
                tasteSettings, timeSettings, otherUser);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("whoknows", "w", "wk", "thosewhoknow")]
    [Summary("Shows what other users listen to an artist in your server")]
    [Examples("w", "wk COMA", "whoknows", "whoknows DJ Seinfeld")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows)]
    public async Task WhoKnowsAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = artistValues,
                DisplayRoleFilter = false
            };

            var settings =
                SettingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context,
                    prfx,
                    contextUser),
                settings.ResponseMode,
                settings.NewSearchValue,
                settings.DisplayRoleFilter,
                redirectsEnabled: settings.RedirectsEnabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("globalwhoknows", "gw", "gwk", "globalwk")]
    [Summary("Shows what other users listen to an artist in .fmbot")]
    [Examples("gw", "gwk COMA", "globalwhoknows", "globalwhoknows DJ Seinfeld")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                HidePrivateUsers = false,
                ShowBotters = false,
                AdminView = false,
                NewSearchValue = artistValues,
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed
            };

            var settings =
                SettingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType, true);

            var response = await artistBuilders
                .GlobalWhoKnowsArtistAsync(new ContextModel(this.Context, prfx, contextUser), settings);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("friendwhoknows", "fw", "fwk")]
    [Summary("Who of your friends know an artist")]
    [Examples("fw", "fwk COMA", "friendwhoknows", "friendwhoknows DJ Seinfeld")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = artistValues
            };

            var settings =
                SettingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await artistBuilders
                .FriendsWhoKnowArtistAsync(new ContextModel(this.Context, prfx, contextUser),
                    currentSettings.ResponseMode, settings.NewSearchValue, settings.RedirectsEnabled);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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

    [Command("serverartists", "sa", "sta", "servertopartists", "serverartist")]
    [Summary("Top artists for your server")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`")]
    [Examples("sa", "sa a p", "serverartists", "serverartists alltime", "serverartists listeners weekly")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists)]
    public async Task GuildArtistsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = extraOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, extraOptions);
        var timeSettings =
            SettingService.GetTimePeriod(extraOptions, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response =
                await artistBuilders.GuildArtistsAsync(new ContextModel(this.Context, prfx), guild,
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

    [Command("affinity", "n", "aff", "neighbors", "soulmates", "neighbours")]
    [Summary("Shows users from this server with similar top artists.")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Artists)]
    public async Task AffinityAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildForWhoKnows(this.Context.Guild.Id);

        try
        {
            var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context,
                firstOptionIsLfmUsername: true);

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

                var message = await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));

                response = await artistBuilders
                    .AffinityAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, guild, guildUsers,
                        largeGuild);

                if (message is Message gatewayMessage)
                {
                    _ = this.Interactivity.SendPaginatorAsync(
                        response.ComponentPaginator.Build(),
                        gatewayMessage,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                }
            }
            else
            {
                response = await artistBuilders
                    .AffinityAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, guild, guildUsers,
                        largeGuild);

                await this.Context.SendResponse(this.Interactivity, response, userService);
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("iceberg", "ice", "icebergify", "berg")]
    [Summary("Shows your iceberg, based on artists popularity.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("iceberg", "iceberg 2024", "iceberg alltime")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Artists)]
    public async Task IcebergAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
                defaultTimePeriod: TimePeriod.AllTime);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await artistBuilders.GetIceberg(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("artistgaps", "gaps", "gap", "artistgap")]
    [Summary("Shows the artists you've returned to after a gap in listening.")]
    [Options(Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("gaps", "artistgaps", "artistgaps quarterly @user", "gaps yearly")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Artists)]
    [SupporterExclusive(
        "To see which artists you've re-discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task ArtistGapsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
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

            var response = await playBuilders.ListeningGapsAsync(context, topListSettings,
                userSettings, mode.mode, GapEntityType.Artist);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
