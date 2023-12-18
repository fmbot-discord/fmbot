using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Builders;

public class UserBuilder
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly TimerService _timer;
    private readonly FeaturedService _featuredService;
    private readonly BotSettings _botSettings;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly PlayService _playService;
    private readonly TimeService _timeService;
    private readonly ArtistsService _artistsService;
    private readonly SupporterService _supporterService;
    private readonly DiscogsService _discogsService;
    private readonly OpenAiService _openAiService;

    public UserBuilder(UserService userService,
        GuildService guildService,
        IPrefixService prefixService,
        TimerService timer,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        IDataSourceFactory dataSourceFactory,
        PlayService playService,
        TimeService timeService,
        ArtistsService artistsService,
        SupporterService supporterService,
        DiscogsService discogsService,
        OpenAiService openAiService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._timer = timer;
        this._featuredService = featuredService;
        this._dataSourceFactory = dataSourceFactory;
        this._playService = playService;
        this._timeService = timeService;
        this._artistsService = artistsService;
        this._supporterService = supporterService;
        this._discogsService = discogsService;
        this._openAiService = openAiService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ResponseModel> GetUserSettings(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithTitle($".fmbot user settings - {context.DiscordUser.GlobalName}");

        response.Embed.WithFooter($"Use '{context.Prefix}configuration' for server-wide settings");

        var settings = new StringBuilder();

        settings.AppendLine($"Connected with Last.fm account [{context.ContextUser.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}). Use `/login` to change.");
        settings.AppendLine();
        settings.AppendLine($"Click the dropdown below to change your user settings.");

        response.Embed.WithDescription(settings.ToString());

        var guildSettings = new SelectMenuBuilder()
            .WithPlaceholder("Select setting to view or change")
            .WithCustomId(InteractionConstants.UserSetting)
            .WithMaxValues(1);

        foreach (var setting in ((UserSetting[])Enum.GetValues(typeof(UserSetting))))
        {
            var name = setting.GetAttribute<OptionAttribute>().Name;
            var description = setting.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(setting);

            guildSettings.AddOption(new SelectMenuOptionBuilder(name, $"us-view-{value}", description));
        }

        response.Components = new ComponentBuilder()
            .WithSelectMenu(guildSettings);

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> FeaturedAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        if (this._timer.CurrentFeatured == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = ".fmbot is still starting up, please try again in a bit..";
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        response.Embed.WithThumbnailUrl(this._timer.CurrentFeatured.ImageUrl);
        response.Embed.AddField("Featured:", this._timer.CurrentFeatured.Description);

        if (context.DiscordGuild != null && guildUsers.Any() && this._timer.CurrentFeatured.UserId.HasValue && this._timer.CurrentFeatured.UserId.Value != 0)
        {
            guildUsers.TryGetValue(this._timer.CurrentFeatured.UserId.Value, out var guildUser);

            if (guildUser != null)
            {
                response.Text = "in-server";

                var dateValue = ((DateTimeOffset)this._timer.CurrentFeatured.DateTime.AddHours(1)).ToUnixTimeSeconds();

                response.Embed.AddField("🥳 Congratulations!",
                    guildUser.DiscordUserId == context.DiscordUser.Id
                        ? $"Oh hey, it's you! You'll be featured until <t:{dateValue}:t>."
                        : $"This user is in this server as **{guildUser.UserName}**.");
            }
        }

        if (this._timer.CurrentFeatured.SupporterDay && context.ContextUser.UserType == UserType.User)
        {
            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
        }

        response.Embed.WithFooter($"View your featured history with '{context.Prefix}featuredlog'");

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
        }

        return response;
    }

    public async Task<ResponseModel> BotScrobblingAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription("Bot scrobbling allows you to automatically scrobble music from Discord music bots to your Last.fm account. " +
                                    "For this to work properly you need to make sure .fmbot can see the voice channel and use a supported music bot.\n\n" +
                                    "Only tracks that already exist on Last.fm will be scrobbled. The bot reads the 'Now Playing' message a bot sends and tries to retrieve the artist and track name from there.\n\n" +
                                    "Currently supported bots:\n" +
                                    "- Cakey Bot\n" +
                                    "- Jockie Music\n" +
                                    "- SoundCloud"
                                    );

        response.Components = new ComponentBuilder()
            .WithButton("Enable", InteractionConstants.BotScrobblingEnable, style: ButtonStyle.Success, new Emoji("✅"))
            .WithButton("Disable", InteractionConstants.BotScrobblingDisable, style: ButtonStyle.Danger, new Emoji("✖️"));

        return response;
    }

    public static ResponseModel FmMode(
        ContextModel context,
        Guild guild = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var fmType = new SelectMenuBuilder()
                .WithPlaceholder("Select embed type")
                .WithCustomId(InteractionConstants.FmCommand.FmSettingType)
                .WithMinValues(1)
                .WithMaxValues(1);

        foreach (var option in ((FmEmbedType[])Enum.GetValues(typeof(FmEmbedType))).OrderBy(o => o.GetAttribute<OptionOrderAttribute>().Order))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(option);

            var active = option == context.ContextUser.FmEmbedType;

            fmType.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
        }

        var maxOptions = context.ContextUser.UserType == UserType.User
            ? Constants.MaxFooterOptions
            : Constants.MaxFooterOptionsSupporter;

        var fmOptions = new SelectMenuBuilder()
            .WithPlaceholder("Select footer options")
            .WithCustomId(InteractionConstants.FmCommand.FmSettingFooter)
            .WithMinValues(0)
            .WithMaxValues(maxOptions);

        var fmSupporterOptions = new SelectMenuBuilder()
            .WithPlaceholder("Select supporter-exclusive footer option")
            .WithCustomId(InteractionConstants.FmCommand.FmSettingFooterSupporter)
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var option in ((FmFooterOption[])Enum.GetValues(typeof(FmFooterOption))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var supporterOnly = option.GetAttribute<OptionAttribute>().SupporterOnly;
            var value = Enum.GetName(option);

            var active = context.ContextUser.FmFooterOptions.HasFlag(option);

            if (!supporterOnly)
            {
                fmOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
            }
            else
            {
                fmSupporterOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
            }
        }

        var builder = new ComponentBuilder()
            .WithSelectMenu(fmType)
            .WithSelectMenu(fmOptions, 1);

        if (context.ContextUser.UserType != UserType.User)
        {
            builder.WithSelectMenu(fmSupporterOptions, 2);
        }

        response.Components = builder;

        response.Embed.WithAuthor("Configuring your 'fm' command");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("Use the dropdowns below to configure how your `fm` command looks.");
        embedDescription.AppendLine();


        embedDescription.Append($"The first dropdown allows you to select a mode, while the second allows you to select up to {maxOptions} options that will be displayed in the footer. ");
        if (context.ContextUser.UserType != UserType.User)
        {
            embedDescription.Append($"The third dropdown lets you select 1 supporter-exclusive option.");
        }

        embedDescription.AppendLine();

        embedDescription.AppendLine();
        embedDescription.Append($"Some options might not always show up on every track, for example when no source data is available. ");

        if (context.ContextUser.UserType == UserType.User)
        {
            embedDescription.Append($"[.fmbot supporters]({Constants.GetSupporterDiscordLink}) can select up to {Constants.MaxFooterOptionsSupporter} options.");
        }

        if (guild?.FmEmbedType != null)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                $"Note that servers can force a specific mode which will override your own mode. ");
            embedDescription.AppendLine(
                $"This server has the **{guild.FmEmbedType}** mode set for everyone, which means your own setting will not apply here.");
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public static ResponseModel ResponseMode(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithAuthor("Configuring your default WhoKnows and top list mode");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var fmType = new SelectMenuBuilder()
                .WithPlaceholder("Select response mode")
                .WithCustomId(InteractionConstants.ResponseModeSetting)
                .WithMinValues(1)
                .WithMaxValues(1);

        foreach (var name in Enum.GetNames(typeof(ResponseMode)).OrderBy(o => o))
        {
            var picked = context.SlashCommand && context.ContextUser.Mode.HasValue && Enum.GetName(context.ContextUser.Mode.Value) == name;

            fmType.AddOption(new SelectMenuOptionBuilder(name, name, isDefault: picked));
        }

        response.Components = new ComponentBuilder()
            .WithSelectMenu(fmType);

        var description = new StringBuilder();

        description.AppendLine("You can also override this when using a command:");
        description.AppendLine("- `image` / `img`");
        description.AppendLine("- `embed`");

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public static ResponseModel ModePick(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Components = new ComponentBuilder()
                .WithButton("'.fm' mode", InteractionConstants.FmCommand.FmModeChange)
                .WithButton("Response mode", InteractionConstants.ResponseModeChange)
        };

        var description = new StringBuilder();

        description.AppendLine("Pick which mode you want to modify:");
        description.AppendLine();
        description.AppendLine("- `fm` mode - Changes how your .fm command looks");
        description.AppendLine("- Response mode - changes default response to `WhoKnows` and top list commands");

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public static ResponseModel Privacy(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var privacySetting = new SelectMenuBuilder()
                .WithPlaceholder("Select Global WhoKnows privacy")
                .WithCustomId(InteractionConstants.FmPrivacySetting)
                .WithMinValues(1)
                .WithMaxValues(1);

        foreach (var name in Enum.GetNames(typeof(PrivacyLevel)).OrderBy(o => o))
        {
            var picked = context.SlashCommand && Enum.GetName(context.ContextUser.PrivacyLevel) == name;

            privacySetting.AddOption(new SelectMenuOptionBuilder(name, name, isDefault: picked));
        }

        var builder = new ComponentBuilder()
            .WithSelectMenu(privacySetting);

        response.Components = builder;

        response.Embed.WithAuthor("Configuring your Global WhoKnows visibility");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();

        response.Embed.AddField("Global",
            "*You are visible everywhere in global WhoKnows with your Last.fm username.*");
        response.Embed.AddField("Server",
            "*You are not visible in global WhoKnows, but users in the same server will still see your name.*");

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public async Task<ResponseModel> FeaturedLogAsync(ContextModel context, UserSettingsModel userSettings, FeaturedView view)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        List<FeaturedLog> featuredHistory;

        var odds = await this._featuredService.GetFeaturedOddsAsync();
        var footer = new StringBuilder();

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        switch (view)
        {
            case FeaturedView.Global:
                {
                    response.Embed.WithTitle($"🌐 Global featured history");

                    featuredHistory = await this._featuredService.GetGlobalFeaturedHistory();
                    break;
                }
            case FeaturedView.Server:
                {
                    response.Embed.WithTitle($"{context.DiscordGuild.Name}'s server featured history");

                    featuredHistory = await this._featuredService.GetFeaturedHistoryForGuild(guildUsers);
                    break;
                }
            case FeaturedView.Friends:
                {
                    featuredHistory = await this._featuredService.GetFeaturedHistoryForFriends(context.ContextUser.UserId);
                    response.Embed.WithTitle(
                        $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s friends featured history");
                    break;
                }
            case FeaturedView.User:
                {
                    featuredHistory = await this._featuredService.GetFeaturedHistoryForUser(userSettings.UserId);
                    response.Embed.WithTitle(
                        $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s featured history");

                    var self = userSettings.DifferentUser ? "They" : "You";

                    if (featuredHistory.Count >= 1)
                    {
                        footer.AppendLine(featuredHistory.Count == 1
                            ? $"{self} have only been featured once. Every hour, that is a chance of 1 in {odds}!"
                            : $"{self} have been featured {featuredHistory.Count} times");
                    }

                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(view), view, null);
        }

        var description = new StringBuilder();
        var nextSupporterSunday = FeaturedService.GetDaysUntilNextSupporterSunday();
        var pages = new List<PageBuilder>();

        if (featuredHistory.Any())
        {
            if (SupporterService.IsSupporter(context.ContextUser.UserType))
            {
                footer.AppendLine($"As a thank you for supporting, you have better odds every first Sunday of the month.");
            }
            else
            {
                footer.AppendLine($"Every first Sunday of the month is Supporter Sunday (in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}). Check '{context.Prefix}getsupporter' for info.");
            }
        }

        var featuredPages = featuredHistory
            .Chunk(5)
            .ToList();

        var pageCounter = 1;

        if (!featuredHistory.Any())
        {
            switch (view)
            {
                case FeaturedView.Global:
                    description.AppendLine("Sorry, nobody has been featured yet.. that is quite strange now that I'm thinking about it 🤨🤨");
                    break;
                case FeaturedView.Server:
                    description.AppendLine("Sorry, nobody in this server has been featured yet..");
                    break;
                case FeaturedView.Friends:
                    description.AppendLine("Sorry, none of your friends have been featured yet..");
                    break;
                case FeaturedView.User:
                    {
                        if (!userSettings.DifferentUser)
                        {
                            description.AppendLine("Sorry, you haven't been featured yet... <:404:882220605783560222>");
                            description.AppendLine();
                            description.AppendLine($"But don't give up hope just yet!");
                            description.AppendLine($"Every hour there is a 1 in {odds} chance that you might be picked.");

                            if (context.DiscordGuild?.Id != this._botSettings.Bot.BaseServerId)
                            {
                                description.AppendLine();
                                description.AppendLine($"Join [our server](https://discord.gg/6y3jJjtDqK) to get pinged if you get featured.");
                            }

                            if (SupporterService.IsSupporter(context.ContextUser.UserType))
                            {
                                description.AppendLine();
                                description.AppendLine($"Also, as a thank you for being a supporter you have a higher chance of becoming featured every first Sunday of the month on Supporter Sunday. The next one is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}.");
                            }
                            else
                            {
                                description.AppendLine();
                                description.AppendLine($"Become an [.fmbot supporter]({Constants.GetSupporterDiscordLink}) and get a higher chance every Supporter Sunday. The next Supporter Sunday is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)} (first Sunday of each month).");
                                response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
                            }
                        }
                        else
                        {
                            description.AppendLine("Hmm, they haven't been featured yet... <:404:882220605783560222>");
                            description.AppendLine();
                            description.AppendLine($"But don't let them give up hope just yet!");
                            description.AppendLine($"Every hour there is a 1 in {odds} chance that they might be picked.");
                        }

                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(view), view, null);
            }

            var page = new PageBuilder()
                .WithDescription(description.ToString())
                .WithTitle(response.Embed.Title)
                .WithFooter(footer.ToString());

            pages.Add(page);
        }
        else
        {
            foreach (var featuredPage in featuredPages)
            {
                foreach (var featured in featuredPage)
                {
                    FullGuildUser guildUser = null;
                    if (view != FeaturedView.User && featured.UserId.HasValue)
                    {
                        guildUsers.TryGetValue(featured.UserId.Value, out guildUser);
                    }

                    description.AppendLine(this._featuredService.GetStringForFeatured(featured, view != FeaturedView.User, guildUser));
                    description.AppendLine();
                }

                var pageFooter = new StringBuilder();
                pageFooter.Append($"Page {pageCounter}/{featuredPages.Count}");
                pageFooter.Append($" - {featuredHistory.Count} total");

                var page = new PageBuilder()
                    .WithDescription(description.ToString())
                    .WithTitle(response.Embed.Title)
                    .WithFooter(pageFooter + "\n" + footer);

                pages.Add(page);

                pageCounter++;
                description = new StringBuilder();
            }

            response.Embed.WithFooter(footer.ToString());
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> ProfileAsync(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        string userTitle;
        var user = context.ContextUser;
        if (userSettings.DifferentUser)
        {
            if (userSettings.DifferentUser && user.DiscordUserId == userSettings.DiscordUserId)
            {
                response.Embed.WithDescription("That user is not registered in .fmbot.");
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }

            userTitle =
                $"{userSettings.DisplayName}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.EmbedAuthor.WithName($"Profile for {userTitle}");
        response.EmbedAuthor.WithUrl($"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}");
        response.Embed.WithAuthor(response.EmbedAuthor);

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);

        if (!string.IsNullOrWhiteSpace(userInfo.Image))
        {
            response.Embed.WithThumbnailUrl(userInfo.Image);
        }

        var description = new StringBuilder();
        if (user.UserType != UserType.User)
        {
            description.AppendLine($"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }
        if (user.DataSource != DataSource.LastFm)
        {
            var name = user.DataSource.GetAttribute<OptionAttribute>().Name;

            switch (user.DataSource)
            {
                case DataSource.FullSpotifyThenLastFm:
                case DataSource.SpotifyThenFullLastFm:
                    description.AppendLine($"Imported: {name}");
                    break;
                case DataSource.LastFm:
                default:
                    break;
            }
        }

        if (description.Length > 0)
        {
            response.Embed.WithDescription(description.ToString());
        }

        var lastFmStats = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(userInfo.Name))
        {
            lastFmStats.AppendLine($"*{userInfo.Name}*");
        }

        lastFmStats.AppendLine($"**[{userSettings.UserNameLastFm}]({LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)})**");
        lastFmStats.AppendLine($"**{userInfo.Country}**");
        lastFmStats.AppendLine($"Since **<t:{userInfo.RegisteredUnix}:D>**");

        if (userInfo.Type != "user")
        {
            if (userInfo.Type == "subscriber")
            {
                lastFmStats.AppendLine("Last.fm Pro subscriber");
            }
            else
            {
                lastFmStats.AppendLine($"Last.fm {userInfo.Type}");
            }
        }

        response.Embed.AddField("Last.fm", lastFmStats.ToString(), true);

        var age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        var totalDays = (DateTime.UtcNow - age).TotalDays;
        var avgPerDay = userInfo.Playcount / totalDays;

        var playcounts = new StringBuilder();
        playcounts.AppendLine($"**{userInfo.Playcount}** scrobbles");
        playcounts.AppendLine($"**{userInfo.TrackCount}** different tracks");
        playcounts.AppendLine($"**{userInfo.AlbumCount}** different albums");
        playcounts.AppendLine($"**{userInfo.ArtistCount}** different artists");
        response.Embed.AddField("Counts", playcounts.ToString(), true);

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        var stats = new StringBuilder();
        if (userSettings.UserType != UserType.User)
        {
            var hasImported = PlayService.UserHasImported(allPlays);
            if (hasImported)
            {
                stats.AppendLine("User has most likely imported plays from external source");
            }
        }

        stats.AppendLine($"Average of **{Math.Round(avgPerDay, 1)}** scrobbles per day");

        stats.AppendLine($"Average of **{Math.Round((double)userInfo.AlbumCount / userInfo.ArtistCount, 1)}** albums and **{Math.Round((double)userInfo.TrackCount / userInfo.ArtistCount, 1)}** tracks per artist");

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);

        if (topArtists.Any())
        {
            var amount = topArtists.OrderByDescending(o => o.UserPlaycount).Take(10).Sum(s => s.UserPlaycount);
            stats.AppendLine($"Top **10** artists make up **{Math.Round((double)amount / userInfo.Playcount * 100, 1)}%** of scrobbles");
        }

        var topDay = allPlays.GroupBy(g => g.TimePlayed.DayOfWeek).MaxBy(o => o.Count());
        if (topDay != null)
        {
            stats.AppendLine($"Most active day of the week is **{topDay.Key.ToString()}**");
        }

        if (stats.Length > 0)
        {
            response.Embed.AddField("Stats", stats.ToString());
        }

        if (user.UserDiscogs != null)
        {
            var collection = new StringBuilder();

            if (user.UserDiscogs.HideValue != true)
            {
                collection.AppendLine($"{user.UserDiscogs.MinimumValue} min " +
                                      $"• {user.UserDiscogs.MedianValue} med " +
                                      $"• {user.UserDiscogs.MaximumValue} max");
            }

            if (user.UserType != UserType.User)
            {
                var discogsCollection = await this._discogsService.GetUserCollection(userSettings.UserId);
                if (discogsCollection.Any())
                {
                    var collectionTypes = discogsCollection
                            .GroupBy(g => g.Release.Format)
                            .OrderByDescending(o => o.Count());
                    foreach (var type in collectionTypes)
                    {
                        collection.AppendLine($"**`{type.Key}` {StringService.GetDiscogsFormatEmote(type.Key)}** - **{type.Count()}** ");
                    }
                }
            }

            if (collection.Length > 0)
            {
                var determiner = userSettings.DifferentUser ? "Their" : "Your";
                response.Embed.AddField($"{determiner} Discogs collection", collection.ToString());
            }
        }

        var footer = new StringBuilder();
        if (user.Friends?.Count > 0)
        {
            footer.AppendLine($"Friends: {user.Friends?.Count}");
        }
        if (user.FriendedByUsers?.Count > 0)
        {
            footer.AppendLine($"Befriended by: {user.FriendedByUsers?.Count}");
        }
        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        response.Components = new ComponentBuilder()
            .WithButton("View history", $"{InteractionConstants.User.History}-{user.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, emote: new Emoji("📖"))
            .WithButton("Last.fm", style: ButtonStyle.Link, url: LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm), emote: Emote.Parse("<:lastfm:882227627287515166>"));

        return response;
    }

    public async Task<ResponseModel> ProfileHistoryAsync(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        string userTitle;
        var user = context.ContextUser;
        if (userSettings.DifferentUser)
        {
            if (userSettings.DifferentUser && context.ContextUser.DiscordUserId == userSettings.DiscordUserId)
            {
                response.Embed.WithDescription("That user is not registered in .fmbot.");
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }

            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.EmbedAuthor.WithName($"History for {userTitle}");
        response.EmbedAuthor.WithUrl($"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}");
        response.Embed.WithAuthor(response.EmbedAuthor);
        var anyHistoryStored = false;

        var description = new StringBuilder();
        if (user.UserType != UserType.User)
        {
            description.AppendLine($"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }
        if (user.DataSource != DataSource.LastFm)
        {
            var name = user.DataSource.GetAttribute<OptionAttribute>().Name;

            switch (user.DataSource)
            {
                case DataSource.FullSpotifyThenLastFm:
                case DataSource.SpotifyThenFullLastFm:
                    description.AppendLine($"Imported: {name}");
                    break;
                case DataSource.LastFm:
                default:
                    break;
            }
        }

        if (this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
        {
            var random = new Random().Next(0, Constants.SupporterPromoChance);
            switch (random)
            {
                case 1:
                    this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                    description.AppendLine($"*Want to see an overview of all your years? [View all perks and get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                    break;
                case 2:
                    this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                    description.AppendLine($"*Want to import and use your Spotify history in .fmbot? [View all perks and get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                    break;
            }
        }

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        var monthDescription = new StringBuilder();
        var monthGroups = allPlays
            .OrderByDescending(o => o.TimePlayed)
            .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

        var processedPlays = 0;
        foreach (var month in monthGroups.Take(6))
        {
            if (userSettings.UserType == UserType.User && processedPlays >= 20000)
            {
                break;
            }

            var time = await this._timeService.GetPlayTimeForPlays(month);
            monthDescription.AppendLine(
                $"**`{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month.Key.Month)}`** " +
                $"- **{month.Count()}** plays " +
                $"- **{StringExtensions.GetLongListeningTimeString(time)}**");
            processedPlays += month.Count();
        }
        if (monthDescription.Length > 0)
        {
            anyHistoryStored = true;
            response.Embed.AddField("Last months", monthDescription.ToString());
        }

        if (userSettings.UserType != UserType.User)
        {
            var yearDescription = new StringBuilder();
            var yearGroups = allPlays
                .OrderByDescending(o => o.TimePlayed)
                .GroupBy(g => g.TimePlayed.Year);

            var totalTime = await this._timeService.GetPlayTimeForPlays(allPlays);
            if (totalTime.TotalSeconds > 0)
            {
                yearDescription.AppendLine(
                    $"` All`** " +
                    $"- **{allPlays.Count}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(totalTime)}");
            }

            foreach (var year in yearGroups)
            {
                var time = await this._timeService.GetPlayTimeForPlays(year);
                yearDescription.AppendLine(
                    $"`{year.Key}`** " +
                    $"- **{year.Count()}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(time)}");
            }
            if (yearDescription.Length > 0)
            {
                anyHistoryStored = true;
                response.Embed.AddField("Years", $"**{yearDescription.ToString()}**");
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                if (user.UserDiscogs == null)
                {
                    response.Embed.AddField("Years", $"*Want to see an overview of your scrobbles throughout the years? " +
                                                     $"[Get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                }
                else
                {
                    response.Embed.AddField("Years", $"*Want to see an overview of your scrobbles throughout the years and your Discogs collection? " +
                                                     $"[Get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                }
            }
        }

        if (!anyHistoryStored)
        {
            if (description.Length > 0)
            {
                description.AppendLine();
            }
            description.AppendLine("*Sorry, it seems like there is no stored data in .fmbot for this user.*");
        }

        response.Embed.WithDescription(description.ToString());

        response.Components = new ComponentBuilder()
            .WithButton("View profile", $"{InteractionConstants.User.Profile}-{user.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, emote: new Emoji("ℹ"))
            .WithButton("Last.fm", style: ButtonStyle.Link, url: LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm), emote: Emote.Parse("<:lastfm:882227627287515166>"));

        return response;
    }

    public async Task<ResponseModel> JudgeAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        UserType userType,
        int usesLeftToday,
        bool differentUserButNotAllowed)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var hasUsesLeft = usesLeftToday > 0;

        var description = new StringBuilder();

        if (hasUsesLeft)
        {
            if (!userSettings.DifferentUser)
            {
                description.AppendLine("Want your music taste to be judged by AI?");
            }
            else
            {
                description.AppendLine($"Judging music taste for **{userSettings.DisplayName}**");
            }

            description.AppendLine("Pick using the buttons below..");
        }

        description.AppendLine();
        if (userType == UserType.User)
        {
            if (hasUsesLeft)
            {
                description.AppendLine($"You can use this command `{usesLeftToday}` more times today.");
            }
            else
            {
                description.Append($"You've ran out of command uses for today, unfortunately the service we use for this is not free. ");
                description.AppendLine($"[Become a supporter]({Constants.GetSupporterDiscordLink}) to raise your daily limit, get access to better responses and the possibility to use the command on others.");

                response.Components = new ComponentBuilder()
                    .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
            }
        }
        else
        {
            if (usesLeftToday is <= 30 and > 0)
            {
                description.AppendLine($"You can use this command `{usesLeftToday}` more times today.");
                description.AppendLine($"Improved AI model has been enabled.");
            }
            if (!hasUsesLeft)
            {
                description.AppendLine($"You've ran out of command uses for today.");
            }
        }
        if (differentUserButNotAllowed)
        {
            description.AppendLine($"*Sorry, only [.fmbot supporters]({Constants.GetSupporterDiscordLink}) can use this command on others.*");
        }
        description.AppendLine();
        description.AppendLine("Some top artists might be sent to OpenAI. No other data is sent.");
        description.AppendLine("Keep in mind that music taste is subjective, and that no matter what this command or anyone else says you're free to like whatever artist you want.");

        if (!timeSettings.DefaultPicked)
        {
            response.Embed.WithFooter($"Time period: {timeSettings.Description}");
        }

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public async Task<ResponseModel> JudgeHandleAsync(ContextModel context,
        UserSettingsModel userSettings,
        InteractiveMessageResult<Item> result,
        List<string> topArtists)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (result.IsTimeout || !result.IsSuccess)
        {
            var embed = new EmbedBuilder()
                .WithDescription("Judgement command timed out. Try again.")
                .WithColor(DiscordConstants.InformationColorBlue);

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = embed.Build();
                x.Components = new ComponentBuilder().Build();
            });

            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var commandUsesLeft = await this._openAiService.GetJudgeUsesLeft(context.ContextUser);

        if (commandUsesLeft <= 0)
        {
            var description = new StringBuilder();
            if (context.ContextUser.UserType == UserType.User)
            {
                description.Append($"You've ran out of command uses for today, unfortunately the service we use for this is not free. ");
                description.AppendLine($"[Become a supporter]({Constants.GetSupporterDiscordLink}) to raise your daily limit and the possibility to use the command on others.");
            }
            else
            {
                description.Append($"You've ran out of command uses for today. ");
            }

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = new EmbedBuilder().WithDescription(description.ToString()).Build();
                x.Components = new ComponentBuilder().Build();
            });

            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var selected = result.Value.Name;
        var descriptor = userSettings.DifferentUser ? $"**{userSettings.DisplayName}**'s" : "your";

        if (selected == "Compliment")
        {
            var embed = new EmbedBuilder()
                .WithDescription($"<a:loading:821676038102056991> Loading {descriptor} compliment...")
                .WithColor(new Color(186, 237, 169));

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = embed.Build();
                x.Components = new ComponentBuilder().Build();
            });

            var complimentResponse = await this.JudgeComplimentAsync(context, userSettings, topArtists);

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = complimentResponse.Embed.Build();
                x.Components = new ComponentBuilder().Build();
            });
        }
        if (selected == "Roast")
        {
            var embed = new EmbedBuilder()
                .WithDescription($"<a:loading:821676038102056991> Loading {descriptor} roast (don't take it personally)...")
                .WithColor(new Color(255, 122, 1));

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = embed.Build();
                x.Components = new ComponentBuilder().Build();
            });

            var complimentResponse = await this.JudgeRoastAsync(context, userSettings, topArtists);

            await result.Message.ModifyAsync(x =>
            {
                x.Embed = complimentResponse.Embed.Build();
                x.Components = new ComponentBuilder().Build();
            });
        }

        return response;
    }

    private async Task<ResponseModel> JudgeComplimentAsync(ContextModel context, UserSettingsModel userSettings, List<string> topArtists)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var supporter = context.ContextUser.UserType != UserType.User;

        var enhanced = supporter ? "enhanced " : null;
        response.Embed.WithAuthor($"{userSettings.DisplayName}'s .fmbot {enhanced}AI judgement - Compliment 🙂");
        response.Embed.WithColor(new Color(186, 237, 169));

        var openAiResponse =
            await this._openAiService.GetJudgeResponse(topArtists, true, supporter);

        if (openAiResponse.Choices == null)
        {
            response.Embed.WithDescription($"<:404:882220605783560222> OpenAI API error - please try again");
            return response;
        }

        var aiGeneration =
            await this._openAiService.StoreAiGeneration(openAiResponse, context.ContextUser.UserId, userSettings.DifferentUser ? userSettings.UserId : null);

        response.Embed.WithDescription(ImproveAiResponse(aiGeneration.Output, topArtists));

        return response;
    }

    private async Task<ResponseModel> JudgeRoastAsync(ContextModel context, UserSettingsModel userSettings, List<string> topArtists)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var supporter = context.ContextUser.UserType != UserType.User;
        var enhanced = supporter ? "enhanced " : null;
        response.Embed.WithAuthor($"{userSettings.DisplayName}'s .fmbot {enhanced}AI judgement - Roast 🔥");
        response.Embed.WithColor(new Color(255, 122, 1));

        var openAiResponse =
            await this._openAiService.GetJudgeResponse(topArtists, false, supporter);

        if (openAiResponse.Choices == null)
        {
            response.Embed.WithDescription($"<:404:882220605783560222> OpenAI API error - please try again");
            return response;
        }

        var aiGeneration =
            await this._openAiService.StoreAiGeneration(openAiResponse, context.ContextUser.UserId, userSettings.DifferentUser ? userSettings.UserId : null);

        response.Embed.WithDescription(ImproveAiResponse(aiGeneration.Output, topArtists));

        return response;
    }

    private static string ImproveAiResponse(string response, List<string> artists)
    {
        response = StringExtensions.Sanitize(response);

        foreach (var artist in artists)
        {
            response = response.Replace(artist, $"*{artist}*");
        }

        return response;
    }

    public static ResponseModel RemoveDataResponse(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        description.AppendLine("**Are you sure you want to delete all your data from .fmbot?**");
        description.AppendLine("This will remove the following data:");

        description.AppendLine("- Account settings like your connected Last.fm account");

        if (context.ContextUser.Friends?.Count > 0)
        {
            var friendString = context.ContextUser.Friends?.Count == 1 ? "friend" : "friends";
            description.AppendLine($"- `{context.ContextUser.Friends?.Count}` {friendString}");
        }

        if (context.ContextUser.FriendedByUsers?.Count > 0)
        {
            var friendString = context.ContextUser.FriendedByUsers?.Count == 1 ? "friendlist" : "friendlists";
            description.AppendLine($"- You from `{context.ContextUser.FriendedByUsers?.Count}` other {friendString}");
        }

        description.AppendLine("- All crowns you've gained or lost");
        description.AppendLine("- All featured history");

        if (context.ContextUser.DataSource != DataSource.LastFm)
        {
            description.AppendLine("- Your Spotify data imports");
        }

        if (context.ContextUser.UserType != UserType.User)
        {
            description.AppendLine($"- `{context.ContextUser.UserType}` account status");
            description.AppendLine("*Account status has to be manually changed back by an .fmbot admin*");
        }

        description.AppendLine();
        description.AppendLine($"Spotify out of sync? Check `/outofsync`");
        description.AppendLine($"Changed Last.fm username? Run `/login`");
        description.AppendLine();


        description.AppendLine($"If you still wish to logout, please click the button below.");

        response.Components = new ComponentBuilder().WithButton("Delete my .fmbot account",
            $"{InteractionConstants.RemoveFmbotAccount}-{context.DiscordUser.Id}", ButtonStyle.Danger);

        response.Embed.WithDescription(description.ToString());

        response.Embed.WithFooter("Note: This will not delete any data from Last.fm, just from .fmbot.");

        response.Embed.WithColor(DiscordConstants.WarningColorOrange);

        return response;
    }

    public static ResponseModel UserReactionsSupporterRequired(ContextModel context, string prfx)
    {
        if (context.ContextUser.UserType != UserType.User)
        {
            return null;
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription($"Only supporters can set their own automatic emoji reactions.\n\n" +
                                        $"[Get supporter here]({Constants.GetSupporterDiscordLink}), or alternatively use the `{prfx}serverreactions` command to set server-wide automatic emoji reactions.");

        response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.CommandResponse = CommandResponse.SupporterRequired;

        return response;
    }

    public static ResponseModel UserReactionsAsync(ContextModel context, string prfx)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        description.Append(
            $"Use the `{prfx}userreactions` command for automatic emoji reacts for `fm` and `featured`. ");
        description.AppendLine("To disable, use without any emojis.");
        description.AppendLine();
        description.AppendLine("Make sure that you have a space between each emoji.");
        description.AppendLine();
        description.AppendLine("Examples:");
        description.AppendLine($"`{prfx}userreactions :PagChomp: :PensiveBlob:`");
        description.AppendLine($"`{prfx}userreactions 😀 😯 🥵`");

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public static ResponseModel ImportMode(ContextModel context, bool hasImported = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var importSetting = new SelectMenuBuilder()
                .WithPlaceholder("Select import setting")
                .WithCustomId(InteractionConstants.ImportSetting)
                .WithMinValues(1)
                .WithMaxValues(1);

        if (!hasImported && context.ContextUser.DataSource == DataSource.LastFm)
        {
            importSetting.IsDisabled = true;
        }

        foreach (var option in ((DataSource[])Enum.GetValues(typeof(DataSource))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(option);

            var active = context.ContextUser.DataSource == option;

            importSetting.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
        }

        response.Components = new ComponentBuilder().WithSelectMenu(importSetting);

        response.Embed.WithAuthor("Configuring your import settings");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("**Last.fm**");
        embedDescription.AppendLine("- Use only your Last.fm for stats and ignore imports");
        embedDescription.AppendLine();

        embedDescription.AppendLine("**Full Spotify, then Last.fm**");
        embedDescription.AppendLine("- Uses your full Spotify history and adds Last.fm afterwards");
        embedDescription.AppendLine("- Recommended if you have imported Spotify onto Last.fm before");
        embedDescription.AppendLine("- Plays from other music apps you scrobbled to Last.fm will not be included");
        embedDescription.AppendLine();

        embedDescription.AppendLine("**Spotify until full Last.fm**");
        embedDescription.AppendLine("- Uses your Spotify history up until the point you started scrobbling on Last.fm");
        embedDescription.AppendLine("- Do not use this if you have imported onto Last.fm before");
        embedDescription.AppendLine("- Best if you have scrobbles on Last.fm from sources other then Spotify");

        if (!hasImported)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine("Run the `/import spotify` command to see how to request your data and to get started with imports. " +
                                        "After importing you'll be able to change these settings.");
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }
}
