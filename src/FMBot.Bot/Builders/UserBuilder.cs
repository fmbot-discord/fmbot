using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
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
    private readonly AdminService _adminService;
    private readonly UpdateService _updateService;
    private readonly IndexService _indexService;
    private readonly ShortcutService _shortcutService;

    private readonly CommandService _commands;

    public UserBuilder(UserService userService,
        GuildService guildService,
        TimerService timer,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        IDataSourceFactory dataSourceFactory,
        PlayService playService,
        TimeService timeService,
        ArtistsService artistsService,
        SupporterService supporterService,
        DiscogsService discogsService,
        OpenAiService openAiService,
        AdminService adminService,
        UpdateService updateService,
        IndexService indexService,
        ShortcutService shortcutService, CommandService commands)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._timer = timer;
        this._featuredService = featuredService;
        this._dataSourceFactory = dataSourceFactory;
        this._playService = playService;
        this._timeService = timeService;
        this._artistsService = artistsService;
        this._supporterService = supporterService;
        this._discogsService = discogsService;
        this._openAiService = openAiService;
        this._adminService = adminService;
        this._updateService = updateService;
        this._indexService = indexService;
        this._shortcutService = shortcutService;
        this._commands = commands;
        this._botSettings = botSettings.Value;
    }

    public static ResponseModel GetUserSettings(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithTitle($".fmbot user settings - {context.DiscordUser.GlobalName}");

        response.Embed.WithFooter($"Use '{context.Prefix}configuration' for server-wide settings");

        var settings = new StringBuilder();

        settings.AppendLine(
            $"Connected with Last.fm account [{context.ContextUser.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}). Use `/login` to change.");
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

        if (this._timer.CurrentFeatured.FullSizeImage == null)
        {
            response.Embed.WithThumbnailUrl(this._timer.CurrentFeatured.ImageUrl);
        }
        else
        {
            response.Embed.WithImageUrl(this._timer.CurrentFeatured.FullSizeImage);
        }

        response.Embed.AddField("Featured:", this._timer.CurrentFeatured.Description);

        if (context.DiscordGuild != null && guildUsers.Any() && this._timer.CurrentFeatured.UserId.HasValue &&
            this._timer.CurrentFeatured.UserId.Value != 0)
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

        if (!string.IsNullOrWhiteSpace(this._timer.CurrentFeatured.ArtistName))
        {
            PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, this._timer.CurrentFeatured.ArtistName);
        }

        if (!string.IsNullOrWhiteSpace(this._timer.CurrentFeatured.AlbumName))
        {
            PublicProperties.UsedCommandsAlbums.TryAdd(context.InteractionId, this._timer.CurrentFeatured.AlbumName);
        }

        if (!string.IsNullOrWhiteSpace(this._timer.CurrentFeatured.TrackName))
        {
            PublicProperties.UsedCommandsTracks.TryAdd(context.InteractionId, this._timer.CurrentFeatured.TrackName);
        }

        if (this._timer.CurrentFeatured.SupporterDay && context.ContextUser.UserType == UserType.User)
        {
            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
                style: ButtonStyle.Secondary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(
                    source: "featured-onsupportersunday"));
        }

        response.Embed.WithFooter($"View your featured history with '{context.Prefix}featuredlog'");

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:",
                "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
        }

        return response;
    }

    public static ResponseModel BotScrobblingAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription(
            "Bot scrobbling allows you to automatically scrobble music from Discord music bots to your Last.fm account. " +
            "For this to work properly fmbot needs to be in the server, you need to make sure it can see the voice channel and you must use a supported music bot.\n\n" +
            "Only tracks that already exist on Last.fm will be scrobbled. The bot reads the 'Now Playing' message a bot sends and tries to retrieve the artist and track name from there.");

        response.Components = new ComponentBuilder()
            .WithButton("Enable", InteractionConstants.BotScrobblingEnable, style: ButtonStyle.Primary)
            .WithButton("Disable", InteractionConstants.BotScrobblingDisable, style: ButtonStyle.Secondary)
            .WithButton("Supported music bots", style: ButtonStyle.Link,
                url: "https://fm.bot/botscrobbling/#currently-supported-bots");

        return response;
    }

    public static ResponseModel StartLogin(User contextUser, string authToken, string publicKey)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var reply = new StringBuilder();
        var link =
            $"https://www.last.fm/api/auth/?api_key={publicKey}&token={authToken}";

        if (contextUser == null)
        {
            reply.AppendLine($"Use the button below to add your Last.fm account to .fmbot.");
            reply.AppendLine();
            reply.AppendLine("This link will expire in 5 minutes, please wait a moment after allowing access...");
        }
        else
        {
            reply.AppendLine(
                $"You have already connected a Last.fm account to the bot. If you want to change or reconnect your connected Last.fm account, **[click here.]({link})** " +
                $"Note that this link will expire after 5 minutes. Also use this link if the bot says you have to re-login.");
            reply.AppendLine();
            reply.AppendLine(
                $"Using Spotify and having problems with your music not being tracked or it lagging behind? " +
                $"Re-logging in again will not fix this, please use `/outofsync` for help instead.");
        }

        response.Embed.WithColor(DiscordConstants.LastFmColorRed);
        response.Embed.WithDescription(reply.ToString());
        response.Components = new ComponentBuilder()
            .WithButton("Connect Last.fm account to .fmbot", style: ButtonStyle.Link, url: link);
        return response;
    }

    public enum LoginState
    {
        SuccessNoIndex,
        SuccessPendingIndex,
        SuccessIndexComplete
    }

    public static ResponseModel LoginSuccess(User newContextUser, LoginState loginState)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
        var description = new StringBuilder();
        switch (loginState)
        {
            case LoginState.SuccessNoIndex:
                description.AppendLine(
                    $"✅ You have been logged in to .fmbot with the username [{newContextUser.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(newContextUser.UserNameLastFM)})!");
                break;
            case LoginState.SuccessPendingIndex:
                description.AppendLine(
                    $"{DiscordConstants.Loading} Fetching Last.fm data for [{newContextUser.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(newContextUser.UserNameLastFM)})...");
                break;
            case LoginState.SuccessIndexComplete:
                description.AppendLine(
                    $"✅ You have been logged in to .fmbot with the username [{newContextUser.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(newContextUser.UserNameLastFM)})!");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(loginState), loginState, null);
        }

        description.AppendLine();
        description.AppendLine(
            $"Use the button below to start configuring your settings and to customize your .fmbot experience.");
        description.AppendLine();
        description.AppendLine($"Please note that .fmbot is not affiliated with Last.fm.");

        response.Components = new ComponentBuilder()
            .WithButton("Settings", style: ButtonStyle.Secondary, customId: InteractionConstants.User.Settings,
                emote: new Emoji("⚙️"))
            .WithButton("Add .fmbot", style: ButtonStyle.Link,
                url: "https://discord.com/oauth2/authorize?client_id=356268235697553409");

        response.Embed.WithDescription(description.ToString());
        return response;
    }

    public static ResponseModel LoginFailure()
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.WarningColorOrange);
        response.Embed.WithDescription(
            $"Login expired or failed. Re-run `/login` to try again.\n\n" +
            $"Still having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.");

        return response;
    }

    public static ResponseModel LoginTooManyAccounts(int altCount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        description.AppendLine(
            $"Can't login, this Last.fm is connected to too many Discord accounts already ({altCount}/{Constants.MaxAlts}).");
        description.AppendLine("");
        description.AppendLine("To delete and transfer data from your other .fmbot accounts:");
        description.AppendLine("1. Use 'Manage alts'");
        description.AppendLine("2. Delete .fmbot accounts you don't use anymore");
        description.AppendLine("3. Login again");
        description.AppendLine("");
        description.AppendLine($"Note that deleting an .fmbot account does not delete any data from Last.fm.");

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.WarningColorOrange);

        response.Components = new ComponentBuilder()
            .WithButton("Manage alts", InteractionConstants.ManageAlts.ManageAltsButton, style: ButtonStyle.Primary);

        return response;
    }

    public static ResponseModel LoginRequired(string prfx, bool alreadyRegistered)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.LastFmColorRed);
        if (alreadyRegistered)
        {
            response.Embed.WithDescription(
                "You have already connected a Last.fm account. To change the account you've connected to .fmbot, use the buttons below.\n\n" +
                $"Using Spotify and having problems with your music not being tracked or it lagging behind? Re-logging in again will not fix this, please use `{prfx}outofsync` for help instead.");
        }
        else
        {
            response.Embed.WithDescription(
                "Welcome to .fmbot. To use .fmbot, a Last.fm account is required.\n\n" +
                "Use the buttons below to sign up or connect your existing Last.fm account.");
        }

        response.Components = GenericEmbedService.UsernameNotSetErrorComponents();

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

        foreach (var option in ((FmEmbedType[])Enum.GetValues(typeof(FmEmbedType))).OrderBy(o =>
                     o.GetAttribute<OptionOrderAttribute>().Order))
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

            if (fmOptions.Options.Count(c => c.IsDefault == true) >= maxOptions)
            {
                active = false;
            }

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

        embedDescription.Append(
            $"The first dropdown allows you to select a mode, while the second allows you to select up to {maxOptions} options that will be displayed in the footer. ");
        if (context.ContextUser.UserType != UserType.User)
        {
            embedDescription.Append($"The third dropdown lets you select 1 supporter-exclusive option.");
        }

        embedDescription.AppendLine();

        embedDescription.AppendLine();
        embedDescription.Append(
            $"Some options might not always show up on every track, for example when no source data is available. ");

        if (context.ContextUser.UserType == UserType.User)
        {
            embedDescription.Append(
                $"[.fmbot supporters]({Constants.GetSupporterDiscordLink}) can select up to {Constants.MaxFooterOptionsSupporter} options.");
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
            var picked = context.SlashCommand && context.ContextUser.Mode.HasValue &&
                         Enum.GetName(context.ContextUser.Mode.Value) == name;

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

    public async Task<ResponseModel> FeaturedLogAsync(ContextModel context, UserSettingsModel userSettings,
        FeaturedView view)
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
                featuredHistory =
                    await this._featuredService.GetFeaturedHistoryForUser(userSettings.UserId,
                        userSettings.UserNameLastFm);
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
                footer.AppendLine(
                    $"As a thank you for supporting, you have better odds every first Sunday of the month.");
            }
            else
            {
                footer.AppendLine(
                    $"Every first Sunday of the month is Supporter Sunday (in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}). Check '{context.Prefix}getsupporter' for info.");
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
                    description.AppendLine(
                        "Sorry, nobody has been featured yet.. that is quite strange now that I'm thinking about it 🤨🤨");
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
                        description.AppendLine(
                            $"Every hour there is a 1 in {odds.Format(context.NumberFormat)} chance that you might be picked.");

                        if (context.DiscordGuild?.Id != this._botSettings.Bot.BaseServerId)
                        {
                            description.AppendLine();
                            description.AppendLine(
                                $"Join [our server](https://discord.gg/6y3jJjtDqK) to get pinged if you get featured.");
                        }

                        if (SupporterService.IsSupporter(context.ContextUser.UserType))
                        {
                            description.AppendLine();
                            description.AppendLine(
                                $"Also, as a thank you for being a supporter you have a higher chance of becoming featured every first Sunday of the month on Supporter Sunday. The next one is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}.");
                        }
                        else
                        {
                            description.AppendLine();
                            description.AppendLine(
                                $"Become an [.fmbot supporter]({Constants.GetSupporterDiscordLink}) and get a higher chance every Supporter Sunday. The next Supporter Sunday is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)} (first Sunday of each month).");
                            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
                                style: ButtonStyle.Secondary,
                                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(
                                    source: "featured-supportersunday"));
                        }
                    }
                    else
                    {
                        description.AppendLine("Hmm, they haven't been featured yet... <:404:882220605783560222>");
                        description.AppendLine();
                        description.AppendLine($"But don't let them give up hope just yet!");
                        description.AppendLine(
                            $"Every hour there is a 1 in {odds.Format(context.NumberFormat)} chance that they might be picked.");
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

                    description.AppendLine(
                        this._featuredService.GetStringForFeatured(featured, view != FeaturedView.User, guildUser));
                    description.AppendLine();
                }

                var pageFooter = new StringBuilder();
                pageFooter.Append($"Page {pageCounter}/{featuredPages.Count.Format(context.NumberFormat)}");
                pageFooter.Append($" - {featuredHistory.Count.Format(context.NumberFormat)} total");

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

        var viewType = new SelectMenuBuilder()
            .WithPlaceholder("Select featured view")
            .WithCustomId(InteractionConstants.FeaturedLog)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((FeaturedView[])Enum.GetValues(typeof(FeaturedView))))
        {
            var name = option.GetAttribute<ChoiceDisplayAttribute>().Name;
            var value = $"{Enum.GetName(option)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}";

            var active = option == view;

            if (option == FeaturedView.Server && context.DiscordGuild == null)
            {
                continue;
            }

            viewType.AddOption(new SelectMenuOptionBuilder(name, value, null, isDefault: active));
        }

        response.StaticPaginator = StringService.BuildStaticPaginatorWithSelectMenu(pages, viewType);
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

            userTitle = userSettings.DisplayName;
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        var initialDescription = new StringBuilder();

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
        if (userTitle.ContainsEmoji())
        {
            initialDescription.AppendLine($"## {userTitle}");
        }
        else
        {
            initialDescription.AppendLine(
                $"## [{userTitle}]({LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)})");
        }

        // initialDescription.AppendLine($"-# {userInfo.Country}");
        initialDescription.AppendLine($"**{userInfo.Playcount.Format(context.NumberFormat)}** scrobbles");
        initialDescription.AppendLine($"Since <t:{userInfo.LfmRegisteredUnix}:D>");
        if (user.UserType != UserType.User)
        {
            initialDescription.AppendLine(
                $"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }

        response.ResponseType = ResponseType.ComponentsV2;

        if (string.IsNullOrWhiteSpace(userInfo.Image))
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(initialDescription.ToString()));
        }
        else
        {
            response.ComponentsContainer.WithSection([
                    new TextDisplayBuilder(initialDescription.ToString())
                ],
                new ThumbnailBuilder(userInfo.Image));
        }


        var playcounts = new StringBuilder();
        if (userInfo.Playcount > 0)
        {
            playcounts.AppendLine($"**{userInfo.TrackCount.Format(context.NumberFormat)}** different tracks");
            playcounts.AppendLine($"**{userInfo.AlbumCount.Format(context.NumberFormat)}** different albums");
            playcounts.AppendLine($"**{userInfo.ArtistCount.Format(context.NumberFormat)}** different artists");
        }

        if (playcounts.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(playcounts.ToString()));
        }

        var discogs = false;
        if (user.UserDiscogs != null)
        {
            var collection = new StringBuilder();
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
                        collection.AppendLine(
                            $" {StringService.GetDiscogsFormatEmote(type.Key)} **{type.Key}** - *{type.Count()} collected* ");
                    }

                    discogs = true;
                }
            }

            if (collection.Length > 0)
            {
                response.ComponentsContainer.AddComponent(new SeparatorBuilder());
                response.ComponentsContainer.AddComponent(new TextDisplayBuilder(collection.ToString()));
            }
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        var totalDays = (DateTime.UtcNow - age).TotalDays;
        var avgPerDay = userInfo.Playcount / totalDays;

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

        stats.AppendLine($"Average of **{Math.Round(avgPerDay, 1).Format(context.NumberFormat)}** scrobbles per day");

        stats.AppendLine(
            $"Average of **{Math.Round((double)userInfo.AlbumCount / userInfo.ArtistCount, 1).Format(context.NumberFormat)}** albums and **{Math.Round((double)userInfo.TrackCount / userInfo.ArtistCount, 1).Format(context.NumberFormat)}** tracks per artist");

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);

        if (topArtists.Any())
        {
            var amount = topArtists.OrderByDescending(o => o.UserPlaycount).Take(10).Sum(s => s.UserPlaycount);
            stats.AppendLine(
                $"Top **10** artists make up **{Math.Round((double)amount / userInfo.Playcount * 100, 1).Format(context.NumberFormat)}%** of scrobbles");
        }

        var topDay = allPlays.GroupBy(g => g.TimePlayed.DayOfWeek).MaxBy(o => o.Count());
        if (topDay != null)
        {
            stats.AppendLine($"Most active day of the week is **{topDay.Key.ToString()}**");
        }

        if (stats.Length > 0 && userInfo.Playcount > 0)
        {
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(stats.ToString()));
        }

        var featuredHistory =
            await this._featuredService.GetFeaturedHistoryForUser(userSettings.UserId, userSettings.UserNameLastFm);

        var footer = new StringBuilder();
        if (user.Friends?.Count > 0)
        {
            footer.Append($"{user.Friends?.Count} friends");
        }

        if (user.FriendedByUsers?.Count > 0)
        {
            if (footer.Length > 0)
            {
                footer.Append($" - ");
            }

            footer.Append($"Befriended by {user.FriendedByUsers?.Count}");
        }

        if (featuredHistory.Count >= 1)
        {
            if (footer.Length > 0)
            {
                footer.Append($" - ");
            }

            footer.Append(
                $"Featured {featuredHistory.Count} {StringExtensions.GetTimesString(featuredHistory.Count)}");
        }

        if (footer.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder($"-# " + footer));
        }

        var actionRow = new ActionRowBuilder();

        actionRow.WithButton("History",
            $"{InteractionConstants.User.History}-{user.DiscordUserId}-{context.ContextUser.DiscordUserId}",
            style: ButtonStyle.Secondary, emote: new Emoji("📖"));

        if (discogs)
        {
            actionRow
                .WithButton("Collection",
                    $"{InteractionConstants.Discogs.Collection}-{user.DiscordUserId}-{context.ContextUser.DiscordUserId}",
                    style: ButtonStyle.Secondary, emote: Emote.Parse(DiscordConstants.Vinyl));
        }

        actionRow
            .WithButton("Last.fm", style: ButtonStyle.Link,
                url: LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm),
                emote: Emote.Parse("<:lastfm:882227627287515166>"));

        response.ComponentsV2.AddComponent(actionRow);

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

            userTitle = userSettings.DisplayName;
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        var initialDescription = new StringBuilder();
        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
        initialDescription.AppendLine(
            $"## [{userTitle}]({LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)})'s history");
        initialDescription.AppendLine($"**{userInfo.Playcount.Format(context.NumberFormat)}** scrobbles");
        initialDescription.AppendLine($"Since <t:{userInfo.LfmRegisteredUnix}:D>");
        if (user.UserType != UserType.User)
        {
            initialDescription.AppendLine(
                $"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }

        if (string.IsNullOrWhiteSpace(userInfo.Image))
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(initialDescription.ToString()));
        }
        else
        {
            response.ComponentsContainer.WithSection([
                    new TextDisplayBuilder(initialDescription.ToString())
                ],
                new ThumbnailBuilder(userInfo.Image));
        }

        var anyHistoryStored = false;

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);
        allPlays = (await this._timeService.EnrichPlaysWithPlayTime(allPlays)).enrichedPlays;

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

            var time = TimeService.GetPlayTimeForEnrichedPlays(month);
            monthDescription.AppendLine(
                $"**`{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month.Key.Month)}`** " +
                $"- **{month.Count().Format(context.NumberFormat)}** plays " +
                $"- **{StringExtensions.GetLongListeningTimeString(time)}**");
            processedPlays += month.Count();
        }

        if (monthDescription.Length > 0)
        {
            anyHistoryStored = true;
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder("**Last months**\n" + monthDescription));
        }

        if (userSettings.UserType != UserType.User)
        {
            var yearDescription = new StringBuilder();
            var yearGroups = allPlays
                .OrderByDescending(o => o.TimePlayed)
                .GroupBy(g => g.TimePlayed.Year);

            var totalTime = TimeService.GetPlayTimeForEnrichedPlays(allPlays);
            if (totalTime.TotalSeconds > 0)
            {
                yearDescription.AppendLine(
                    $"**` All`** " +
                    $"- **{allPlays.Count.Format(context.NumberFormat)}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(totalTime)}**");
            }

            foreach (var year in yearGroups)
            {
                var time = TimeService.GetPlayTimeForEnrichedPlays(year);
                yearDescription.AppendLine(
                    $"**`{year.Key}`** " +
                    $"- **{year.Count()}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(time)}**");
            }

            if (yearDescription.Length > 0)
            {
                anyHistoryStored = true;
                response.ComponentsContainer.AddComponent(new SeparatorBuilder());
                response.ComponentsContainer.AddComponent(new TextDisplayBuilder("**All years**\n" + yearDescription));
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 &&
                this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType,
                    context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                if (user.UserDiscogs == null)
                {
                    response.Embed.AddField("Years",
                        $"*Want to see an overview of your scrobbles throughout the years? " +
                        $"[Get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                }
                else
                {
                    response.Embed.AddField("Years",
                        $"*Want to see an overview of your scrobbles throughout the years and your Discogs collection? " +
                        $"[Get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})*");
                }
            }
        }

        if (!anyHistoryStored)
        {
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(
                new TextDisplayBuilder("*Sorry, it seems like there is no stored data in .fmbot for this user.*"));
        }
        else
        {
            if (user.DataSource != DataSource.LastFm)
            {
                var name = user.DataSource.GetAttribute<OptionAttribute>().Name;

                switch (user.DataSource)
                {
                    case DataSource.FullImportThenLastFm:
                    case DataSource.ImportThenFullLastFm:
                        response.ComponentsContainer.AddComponent(new SeparatorBuilder());
                        response.ComponentsContainer.AddComponent(
                            new TextDisplayBuilder($"{DiscordConstants.Imports} .fmbot imports: {name}"));
                        break;
                    case DataSource.LastFm:
                    default:
                        break;
                }
            }
        }

        var actionRow = new ActionRowBuilder();

        actionRow
            .WithButton("Profile",
                $"{InteractionConstants.User.Profile}-{user.DiscordUserId}-{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: new Emoji("ℹ"))
            .WithButton("Last.fm", style: ButtonStyle.Link,
                url: LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm),
                emote: Emote.Parse("<:lastfm:882227627287515166>"));

        response.ComponentsV2.AddComponent(actionRow);

        return response;
    }

    public static ResponseModel JudgeAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        UserType userType,
        (int amount, bool show, int amountThisWeek) usesLeftToday,
        bool differentUserButNotAllowed)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var hasUsesLeft = usesLeftToday.amount > 0;

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

        if (!SupporterService.IsSupporter(userType))
        {
            if (hasUsesLeft)
            {
                if (usesLeftToday.show)
                {
                    description.AppendLine();
                    description.AppendLine($"You can use this command `{usesLeftToday.amount}` more times today.");
                }
            }
            else
            {
                description.AppendLine();
                description.Append(
                    $"You've ran out of command uses for today, unfortunately the service we use for this is not free. ");
                description.AppendLine(
                    $"[Become a supporter]({Constants.GetSupporterDiscordLink}) to raise your daily limit, get access to better responses and the possibility to use the command on others.");

                response.Components = new ComponentBuilder()
                    .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                        customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(
                            source: "judge-dailylimit"));
            }
        }
        else
        {
            description.AppendLine();

            if (usesLeftToday.amount is <= 30 and > 0 && usesLeftToday.show)
            {
                description.AppendLine($"You can use this command `{usesLeftToday.amount}` more times today.");
            }

            description.AppendLine($"⭐ Supporter perk: Using premium AI model");

            if (!hasUsesLeft)
            {
                description.AppendLine($"You've ran out of command uses for today.");
            }
        }

        if (differentUserButNotAllowed)
        {
            description.AppendLine();
            description.AppendLine(
                $"*Sorry, only [.fmbot supporters]({Constants.GetSupporterDiscordLink}) can use this command on others.*");
        }

        if (!timeSettings.DefaultPicked)
        {
            response.Embed.WithFooter($"Time period: {timeSettings.Description}");
        }

        description.AppendLine();
        description.AppendLine(
            "-# Keep in mind that music taste is subjective, and that no matter what this command or anyone else says you're free to like whatever artist you want.");

        response.Embed.WithDescription(description.ToString());

        if (hasUsesLeft)
        {
            response.Components = new ComponentBuilder()
                .WithButton("Compliment", emote: new Emoji("🙂"), style: ButtonStyle.Primary,
                    customId:
                    $"{InteractionConstants.Judge}~{timeSettings.Description}~compliment~{userSettings.DiscordUserId}~{context.DiscordUser.Id}")
                .WithButton("Roast", emote: new Emoji("🔥"), style: ButtonStyle.Primary,
                    customId:
                    $"{InteractionConstants.Judge}~{timeSettings.Description}~roast~{userSettings.DiscordUserId}~{context.DiscordUser.Id}");
        }

        return response;
    }

    public async Task<ResponseModel> JudgeHandleAsync(ContextModel context,
        UserSettingsModel userSettings,
        string selected,
        List<TopArtist> topArtists, List<TopTrack> topTracks)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var commandUsesLeft = await this._openAiService.GetJudgeUsesLeft(context.ContextUser);

        if (commandUsesLeft.amount <= 0)
        {
            var description = new StringBuilder();
            if (context.ContextUser.UserType == UserType.User)
            {
                description.Append(
                    $"You've ran out of command uses for today, unfortunately the service we use for this is not free. ");
                description.AppendLine(
                    $"[Become a supporter]({Constants.GetSupporterDiscordLink}) to raise your daily limit and the possibility to use the command on others.");
            }
            else
            {
                description.Append($"You've ran out of command uses for today. ");
            }

            response.Embed.WithDescription(description.ToString());
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        response = selected switch
        {
            "compliment" => await this.JudgeComplimentAsync(context, userSettings, topArtists, topTracks,
                commandUsesLeft.amountThisWeek),
            "roast" => await this.JudgeRoastAsync(context, userSettings, topArtists, topTracks,
                commandUsesLeft.amountThisWeek),
            _ => response
        };

        return response;
    }

    private async Task<ResponseModel> JudgeComplimentAsync(ContextModel context, UserSettingsModel userSettings,
        List<TopArtist> topArtists, List<TopTrack> topTracks, int amountThisWeek)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var supporter = context.ContextUser.UserType != UserType.User;

        var enhanced = supporter ? " - Premium model ⭐" : null;
        response.Embed.WithAuthor($"{userSettings.DisplayName}'s .fmbot AI judgement - Compliment 🙂{enhanced}");
        response.Embed.WithColor(new Color(186, 237, 169));

        await this._openAiService.StoreAiGeneration(context.InteractionId, context.ContextUser.UserId,
            userSettings.DifferentUser ? userSettings.UserId : null);

        var openAiResponse =
            await this._openAiService.GetJudgeResponse(topArtists, topTracks, PromptType.Compliment, amountThisWeek,
                supporter);

        if (openAiResponse.Output == null)
        {
            response.Embed.WithDescription($"<:404:882220605783560222> OpenAI API error - please try again");
            return response;
        }

        var aiGeneration =
            await this._openAiService.UpdateAiGeneration(context.InteractionId, openAiResponse);

        response.Embed.WithDescription(aiGeneration.Output);

        return response;
    }

    private async Task<ResponseModel> JudgeRoastAsync(ContextModel context, UserSettingsModel userSettings,
        List<TopArtist> topArtists, List<TopTrack> topTracks, int amountThisWeek)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var supporter = context.ContextUser.UserType != UserType.User;
        var enhanced = supporter ? " - Premium model ⭐" : null;
        response.Embed.WithAuthor($"{userSettings.DisplayName}'s .fmbot AI judgement - Roast 🔥{enhanced}");
        response.Embed.WithColor(new Color(255, 122, 1));

        await this._openAiService.StoreAiGeneration(context.InteractionId, context.ContextUser.UserId,
            userSettings.DifferentUser ? userSettings.UserId : null);

        var openAiResponse =
            await this._openAiService.GetJudgeResponse(topArtists, topTracks, PromptType.Roast, amountThisWeek,
                supporter);

        if (openAiResponse.Output == null)
        {
            response.Embed.WithDescription($"<:404:882220605783560222> OpenAI API error - please try again");
            return response;
        }

        var aiGeneration =
            await this._openAiService.UpdateAiGeneration(context.InteractionId, openAiResponse);

        response.Embed.WithDescription(aiGeneration.Output);

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

        description.AppendLine("- .fmbot settings and data");

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
        if (context.ContextUser.DataSource != DataSource.LastFm)
        {
            description.AppendLine("- Your Spotify and/or Apple Music data imports");
        }

        if (context.ContextUser.UserType != UserType.User)
        {
            description.AppendLine($"- `{context.ContextUser.UserType}` account status");
            if (context.ContextUser.UserType == UserType.Supporter)
            {
                description.AppendLine(
                    "-# *If you have supporter purchased through Discord or Stripe and plan to create a new .fmbot account with the same Discord account, your status will re-apply in a few minutes. Supporter purchased through Discord can't be moved to different Discord accounts." +
                    "If you are a supporter purchased though OpenCollective, please open a help thread on [our server](https://discord.gg/fmbot) for account transfers.*");
            }
            else
            {
                description.AppendLine("*⚠️ Account status has to be manually changed back by an .fmbot admin*");
            }
        }

        description.AppendLine();
        description.AppendLine($"Spotify out of sync? Check `{context.Prefix}outofsync`");
        description.AppendLine($"Changed Last.fm username? Run `{context.Prefix}login`");
        description.AppendLine($"Data not matching Last.fm profile? Run `{context.Prefix}update full`");
        description.AppendLine();

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

        response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
            style: ButtonStyle.Primary,
            customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "userreactions"));

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.CommandResponse = CommandResponse.SupporterRequired;

        return response;
    }

    public static ResponseModel UserReactions(ContextModel context, string prfx)
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

    public static ResponseModel Localization(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        description.AppendLine(
            "Use the `/localization` command to set your timezone and number formatting for .fmbot commands. ");
        description.AppendLine();
        description.AppendLine("Pick your timezone and format through the option in the slash command.");
        description.AppendLine();
        description.Append(
            "*Note: This does not update the localization setting on the Last.fm website. You can do that [here](https://www.last.fm/settings/website). ");
        description.AppendLine("The bot is not affiliated with Last.fm.*");

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> ImportMode(ContextModel context, int userId)
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

        var allPlays = await this._playService.GetAllUserPlays(userId, false);
        var hasImported = allPlays.Any(a =>
            a.PlaySource == PlaySource.SpotifyImport || a.PlaySource == PlaySource.AppleMusicImport);

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

        response.Embed.WithAuthor("Configuring how imports are combined with your Last.fm");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var importSource = "import data";
        if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport) &&
            allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Apple Music & Spotify";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
        {
            importSource = "Apple Music";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Spotify";
        }

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("**Last.fm**");
        embedDescription.AppendLine("- Use only your Last.fm for stats and ignore imports");
        embedDescription.AppendLine(
            $"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm).Format(context.NumberFormat)} Last.fm scrobbles");
        embedDescription.AppendLine();

        embedDescription.AppendLine($"**Full Imports, then Last.fm**");
        embedDescription.AppendLine($"- Uses your full {importSource} history and adds Last.fm afterwards");
        embedDescription.AppendLine("- Plays from other music apps you scrobbled to Last.fm will not be included");

        var playsWithFullImportThenLastFm =
            await this._playService.GetPlaysWithDataSource(userId, DataSource.FullImportThenLastFm);
        embedDescription.Append(
            $"- {playsWithFullImportThenLastFm.Count(c => c.PlaySource == PlaySource.SpotifyImport || c.PlaySource == PlaySource.AppleMusicImport).Format(context.NumberFormat)} imports + ");
        embedDescription.Append(
            $"{playsWithFullImportThenLastFm.Count(c => c.PlaySource == PlaySource.LastFm).Format(context.NumberFormat)} scrobbles = ");
        embedDescription.Append($"{playsWithFullImportThenLastFm.Count().Format(context.NumberFormat)} plays");
        embedDescription.AppendLine();
        embedDescription.AppendLine();

        embedDescription.AppendLine($"**Imports until full Last.fm**");
        embedDescription.AppendLine(
            $"- Uses your {importSource} history up until the point you started scrobbling on Last.fm");
        embedDescription.AppendLine($"- Best if you have scrobbles on Last.fm from sources other then {importSource}");

        var playsWithImportUntilFullLastFm =
            await this._playService.GetPlaysWithDataSource(userId, DataSource.ImportThenFullLastFm);
        embedDescription.Append(
            $"- {playsWithImportUntilFullLastFm.Count(c => c.PlaySource == PlaySource.SpotifyImport || c.PlaySource == PlaySource.AppleMusicImport).Format(context.NumberFormat)} imports + ");
        embedDescription.Append(
            $"{playsWithImportUntilFullLastFm.Count(c => c.PlaySource == PlaySource.LastFm).Format(context.NumberFormat)} scrobbles = ");
        embedDescription.Append($"{playsWithImportUntilFullLastFm.Count().Format(context.NumberFormat)} plays");
        embedDescription.AppendLine();

        if (!hasImported)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                "Run the `.import` command to see how to request your data and to get started with imports. " +
                "After importing you'll be able to change these settings.");
        }
        else
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine($"**Total counts**");
            if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
            {
                embedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport).Format(context.NumberFormat)} imported Apple Music plays");
            }

            if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
            {
                embedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport).Format(context.NumberFormat)} imported Spotify plays");
            }

            embedDescription.AppendLine(
                $"- {allPlays.Count(c => c.PlaySource == PlaySource.LastFm).Format(context.NumberFormat)} Last.fm scrobbles");
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public async Task<ResponseModel> ManageAlts(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        if (context.ContextUser.SessionKeyLastFm == null)
        {
            response.Embed.SessionRequiredResponse(context.Prefix);
            response.CommandResponse = CommandResponse.UsernameNotSet;
            return response;
        }

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        var alts = await this._adminService.GetUsersWithLfmUsernameAsync(context.ContextUser.UserNameLastFM);

        if (alts.Count > 1)
        {
            var altSelector = new SelectMenuBuilder()
                .WithPlaceholder("Select alt to manage")
                .WithCustomId(InteractionConstants.ManageAlts.ManageAltsPicker)
                .WithMinValues(1)
                .WithMaxValues(1);

            var amount = 1;
            foreach (var alt in alts
                         .OrderByDescending(o => o.LastUsed)
                         .ThenByDescending(o => o.UserId)
                         .Where(w => w.UserId != context.ContextUser.UserId)
                         .Take(25))
            {
                var description = new StringBuilder();

                var displayName = alt.DiscordUserId.ToString();
                if (amount <= 5)
                {
                    var user = await this._userService.GetUserFromDiscord(alt.DiscordUserId);
                    if (user != null)
                    {
                        displayName = user.Username;
                        if (user.GlobalName != null)
                        {
                            description.Append($"{user.GlobalName}");
                        }
                    }
                }

                if (alt.LastUsed.HasValue)
                {
                    if (description.Length > 0)
                    {
                        description.Append($" - ");
                    }

                    description.Append($"Last used {StringExtensions.GetTimeAgoShortString(alt.LastUsed.Value)} ago");
                }

                altSelector.AddOption(description.Length > 0
                    ? new SelectMenuOptionBuilder(displayName, alt.UserId.ToString(), description.ToString())
                    : new SelectMenuOptionBuilder(displayName, alt.UserId.ToString()));

                amount++;
            }

            response.Components = new ComponentBuilder().WithSelectMenu(altSelector);
        }
        else
        {
            response.Embed.AddField("No alts found",
                "You don't have any other .fmbot accounts with the same Last.fm username.");
        }

        response.Embed.WithTitle("Manage other .fmbot accounts");

        var embedDescription = new StringBuilder();
        embedDescription.AppendLine("Manage your other .fmbot accounts that are connected to the same Last.fm user.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("When you select an account you have the following options:");
        embedDescription.AppendLine("- Transfer data (streaks, featured history and imports) and delete account");
        embedDescription.AppendLine("- Only delete account");
        embedDescription.AppendLine();
        embedDescription.AppendLine(
            ".fmbot is not affiliated with Last.fm. No Last.fm data can be modified, transferred or deleted with this command.");

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public ResponseModel ManageLinkedRoles(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Embed.WithDescription("Use the link below to authorize .fmbot.\n\n" +
                                       "If a server has any linked roles available, you can claim them by clicking the server name and going to 'Linked roles'.");
        response.Components = new ComponentBuilder()
            .WithButton("Authorize .fmbot", style: ButtonStyle.Link, url: this._botSettings.Discord.InstallUri)
            .WithButton("Refresh linked data", style: ButtonStyle.Secondary, customId: "update-linkedroles");

        return response;
    }

    public static ResponseModel UpdatePlaysInit(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription(
            $"<a:loading:821676038102056991> Fetching **{context.ContextUser.UserNameLastFM}**'s latest scrobbles...");

        return response;
    }

    public async Task<ResponseModel> UpdatePlays(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var update = await this._updateService.UpdateUserAndGetRecentTracks(context.ContextUser);

        var updatePromo =
            await this._supporterService.GetPromotionalUpdateMessage(context.ContextUser, context.Prefix,
                context.DiscordGuild?.Id);
        var upgradeButton = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
            style: ButtonStyle.Secondary,
            customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: updatePromo.supporterSource));

        if (GenericEmbedService.RecentScrobbleCallFailed(update))
        {
            return GenericEmbedService.RecentScrobbleCallFailedResponse(update, context.ContextUser.UserNameLastFM);
        }

        var updatedDescription = new StringBuilder();

        if (update.Content.NewRecentTracksAmount == 0 && update.Content.RemovedRecentTracksAmount == 0)
        {
            var previousUpdate = DateTime.SpecifyKind(context.ContextUser.LastUpdated.Value, DateTimeKind.Utc);
            var previousUpdateValue = ((DateTimeOffset)previousUpdate).ToUnixTimeSeconds();

            updatedDescription.AppendLine(
                $"Nothing new found on [your Last.fm profile]({LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}) since the last check <t:{previousUpdateValue}:R>.");

            if (update.Content?.RecentTracks != null && update.Content.RecentTracks.Any())
            {
                if (!update.Content.RecentTracks.Any(a => a.NowPlaying))
                {
                    var latestScrobble = update.Content.RecentTracks.MaxBy(o => o.TimePlayed);
                    if (latestScrobble != null && latestScrobble.TimePlayed.HasValue)
                    {
                        var specifiedDateTime = DateTime.SpecifyKind(latestScrobble.TimePlayed.Value, DateTimeKind.Utc);
                        var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();
                        updatedDescription.AppendLine();
                        updatedDescription.AppendLine($"Your last scrobble was <t:{dateValue}:R>.");
                    }

                    updatedDescription.AppendLine();
                    updatedDescription.AppendLine(
                        $"Last.fm not keeping track of your Spotify properly? Try the instructions in `{context.Prefix}outofsync` for help.");
                }
            }
            else
            {
                if (updatePromo.message != null)
                {
                    updatedDescription.AppendLine();
                    updatedDescription.AppendLine(updatePromo.message);
                    if (updatePromo.showUpgradeButton)
                    {
                        response.Components = upgradeButton;
                    }
                }
            }

            response.Embed =
                new EmbedBuilder()
                    .WithDescription(updatedDescription.ToString())
                    .WithColor(DiscordConstants.SuccessColorGreen);
        }
        else
        {
            if (update.Content.RemovedRecentTracksAmount == 0)
            {
                updatedDescription.AppendLine(
                    $"✅ Cached playcounts have been updated for {context.ContextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount.Format(context.NumberFormat)} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)}.");
            }
            else
            {
                updatedDescription.AppendLine(
                    $"✅ Cached playcounts have been updated for {context.ContextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount.Format(context.NumberFormat)} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)} " +
                    $"and {update.Content.RemovedRecentTracksAmount} removed {StringExtensions.GetScrobblesString(update.Content.RemovedRecentTracksAmount)}.");
            }

            if (updatePromo.message != null)
            {
                updatedDescription.AppendLine();
                updatedDescription.AppendLine(updatePromo.message);
                if (updatePromo.showUpgradeButton)
                {
                    response.Components = upgradeButton;
                }
            }

            response.Embed = new EmbedBuilder()
                .WithDescription(updatedDescription.ToString())
                .WithColor(DiscordConstants.SuccessColorGreen);
        }

        await this._userService.UpdateLinkedRole(context.ContextUser.DiscordUserId);

        return response;
    }

    public ResponseModel UpdateOptionsInit(ContextModel context, UpdateType updateType, string updateTypeDescription)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (PublicProperties.IssuesAtLastFm)
        {
            var issueDescription = new StringBuilder();

            issueDescription.AppendLine(
                "Doing an advanced update is disabled temporarily while Last.fm is having issues. Please try again later.");
            if (PublicProperties.IssuesReason != null)
            {
                issueDescription.AppendLine();
                issueDescription.AppendLine("Note:");
                issueDescription.AppendLine($"*{PublicProperties.IssuesReason}*");
            }

            response.Embed.WithDescription(issueDescription.ToString());
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        if (context.ContextUser.LastIndexed > DateTime.UtcNow.AddMinutes(-30))
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription(
                "You can't do full updates too often. These are only meant to be used when your Last.fm history has been adjusted.\n\n" +
                $"Using Spotify and having problems with your music not being tracked or it lagging behind? Please use `{context.Prefix}outofsync` for help. Spotify sync issues can't be fixed inside of .fmbot.");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var indexAlreadyStarted = this._indexService.IndexStarted(context.ContextUser.UserId);

        if (!indexAlreadyStarted)
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription(
                "An advanced update has recently already been started for you. Please wait before starting a new one.");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var indexDescription = new StringBuilder();
        indexDescription.AppendLine(
            $"<a:loading:821676038102056991> Fetching Last.fm playcounts for user {context.ContextUser.UserNameLastFM}...");
        indexDescription.AppendLine();
        indexDescription.AppendLine("The following playcount caches are being rebuilt:");
        indexDescription.AppendLine(updateTypeDescription);

        if (context.ContextUser.UserType != UserType.User)
        {
            indexDescription.AppendLine(
                $"*Thanks for being an .fmbot {context.ContextUser.UserType.ToString().ToLower()}. " +
                $"Your full Last.fm history will now be cached, so this command might take slightly longer...*");
        }

        response.Embed.WithDescription(indexDescription.ToString());

        return response;
    }

    public async Task<ResponseModel> UpdateOptions(ContextModel context, UpdateType updateType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (!updateType.HasFlag(UpdateType.Full) && !updateType.HasFlag(UpdateType.AllPlays))
        {
            var update =
                await this._updateService.UpdateUserAndGetRecentTracks(context.ContextUser, bypassIndexPending: true);

            if (GenericEmbedService.RecentScrobbleCallFailed(update))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(update, context.ContextUser.UserNameLastFM);
            }
        }

        var result = await this._indexService.ModularUpdate(context.ContextUser, updateType);

        var description = UserService.GetIndexCompletedUserStats(context.ContextUser, result, context.NumberFormat);

        response.Embed = new EmbedBuilder()
            .WithDescription(description.description)
            .WithColor(result.UpdateError != true
                ? DiscordConstants.SuccessColorGreen
                : DiscordConstants.WarningColorOrange);
        response.Components = description.promo
            ? new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Secondary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "update-alldata"))
            : null;

        await this._userService.UpdateLinkedRole(context.DiscordUser.Id);

        return response;
    }

    public static ResponseModel ShortcutsSupporterRequired(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (!SupporterService.IsSupporter(context.ContextUser.UserType))
        {
            response.Embed.WithDescription(
                "Command shortcuts are only available for .fmbot supporters.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "user-shortcuts"));
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public async Task<ResponseModel> ListShortcutsAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var shortcuts = await _shortcutService.GetUserShortcuts(context.ContextUser);

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        var name = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);
        response.ComponentsContainer.AddComponent(new TextDisplayBuilder($"## <:shortcut:1416430054061117610> {name}'s command shortcuts"));
        var prfx = context.Prefix == "/" ? "." : context.Prefix;

        if (shortcuts.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new SeparatorBuilder());
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder("You haven't set up any shortcuts yet.\n\n" +
                                                                             $"Make sure you don't include the prefix ({prfx}) when creating shortcuts."));
        }
        else
        {
            foreach (var shortcut in shortcuts)
            {
                response.ComponentsContainer.AddComponent(new SeparatorBuilder());
                response.ComponentsContainer.AddComponent(new SectionBuilder
                {
                    Components =
                    [
                        new TextDisplayBuilder(
                            $"**Input:** `{StringExtensions.Sanitize(shortcut.Input)}`\n**Output:** `{StringExtensions.Sanitize(shortcut.Output)}`")
                    ],
                    Accessory = new ButtonBuilder("Edit", style: ButtonStyle.Secondary,
                        customId: $"{InteractionConstants.Shortcuts.Manage}-{shortcut.Id}")
                });
            }
        }

        response.ComponentsContainer.AddComponent(new SeparatorBuilder());
        response.ComponentsContainer.AddComponent(new SectionBuilder
        {
            Components =
            [
                new TextDisplayBuilder(
                    $"-# {shortcuts.Count}/10 shortcut slots used\n" +
                    $"-# Any change takes a minute to apply in other servers")
            ],
            Accessory = new ButtonBuilder("Create", style: ButtonStyle.Primary,
                customId: $"{InteractionConstants.Shortcuts.Create}-{context.DiscordUser.Id}")
        });

        return response;
    }

    public async Task<ResponseModel> ManageShortcutAsync(ContextModel context, int shortcutId, ulong overviewMessageId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var shortcut = await _shortcutService.GetUserShortcut(shortcutId);

        if (shortcut == null)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription("This shortcut doesn't exist.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        if (context.ContextUser.UserId != shortcut.UserId)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription("This isn't your shortcut, you can only edit your own shortcuts.");
            response.CommandResponse = CommandResponse.NoPermission;
            return response;
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        var description = new StringBuilder();
        description.AppendLine($"**Input:** `{shortcut.Input}`");
        description.AppendLine($"**Output:** `{shortcut.Output}`");
        response.ComponentsContainer.AddComponent(new TextDisplayBuilder(description.ToString()));

        response.ComponentsContainer.AddComponent(new SeparatorBuilder());
        var actionRow = new ActionRowBuilder();
        actionRow.AddComponent(new ButtonBuilder("Modify", style: ButtonStyle.Secondary,
            customId: $"{InteractionConstants.Shortcuts.Modify}-{shortcut.Id}-{overviewMessageId}"));
        actionRow.AddComponent(new ButtonBuilder("Delete", style: ButtonStyle.Danger,
            customId: $"{InteractionConstants.Shortcuts.Delete}-{shortcut.Id}-{overviewMessageId}"));
        response.ComponentsContainer.AddComponent(actionRow);

        return response;
    }

    public async Task<ResponseModel> CreateShortcutAsync(ContextModel context, string input, string output)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var shortcuts = await _shortcutService.GetUserShortcuts(context.ContextUser);
            var validatedInput = this.ValidateShortcut(response, shortcuts, input, output);
            if (!validatedInput.validated)
            {
                return validatedInput.response;
            }

            await _shortcutService.AddOrUpdateUserShortcut(context.ContextUser, 0, input, output);
            return null;
        }
        catch (Exception ex)
        {
            response.Embed.WithDescription($"❌ Error while trying to create shortcut");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    public async Task<ResponseModel> ModifyShortcutAsync(ContextModel context, int shortcutId, string input, string output)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var shortcuts = await _shortcutService.GetUserShortcuts(context.ContextUser);
            var shortcut = shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null)
            {
                response.Embed.WithDescription("❌ Shortcut not found.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            var validatedInput = this.ValidateShortcut(response, shortcuts, input, output, shortcutId);
            if (!validatedInput.validated)
            {
                return validatedInput.response;
                ;
            }

            await _shortcutService.AddOrUpdateUserShortcut(context.ContextUser, shortcutId, input, output);
            return null;
        }
        catch (Exception ex)
        {
            response.Embed.WithDescription($"❌ Failed to modify shortcut: {ex.Message}");
            response.Embed.WithColor(DiscordConstants.LastFmColorRed);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    public async Task<UserShortcut> GetShortcut(int shortcutId)
    {
        return await this._shortcutService.GetUserShortcut(shortcutId);
    }

    public async Task<ResponseModel> DeleteShortcutAsync(ContextModel context, int shortcutId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var shortcut = await _shortcutService.GetUserShortcut(shortcutId);

            if (shortcut == null)
            {
                response.Embed.WithDescription("❌ Shortcut not found.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            if (context.ContextUser.UserId != shortcut.UserId)
            {
                response.Embed.WithDescription("❌ You can only delete your own shortcuts.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NoPermission;
                return response;
            }

            await _shortcutService.RemoveUserShortcut(context.ContextUser, shortcut.Input);
            return null;
        }
        catch (Exception ex)
        {
            response.Embed.WithDescription($"❌ Failed to delete shortcut: {ex.Message}");
            response.Embed.WithColor(DiscordConstants.LastFmColorRed);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    private (bool validated, ResponseModel response) ValidateShortcut(ResponseModel response,
        List<UserShortcut> existingShortcuts,
        string input,
        string output,
        int currentShortcutId = 0)
    {
        if (existingShortcuts.Count >= 10)
        {
            response.Embed.WithDescription($"❌ You can't create more then 10 shortcuts");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Cooldown;
            return (false, response);
        }

        if (existingShortcuts.Any(a => a.Id != currentShortcutId &&
                                       string.Equals(a.Input, input, StringComparison.OrdinalIgnoreCase)))
        {
            response.Embed.WithDescription($"❌ You already have a shortcut with this input");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        var inputCommands = this._commands.Search(input);
        if (inputCommands.IsSuccess && inputCommands.Commands.Any(a => a.Command.Name.Equals("shortcuts")))
        {
            response.Embed.WithDescription($"❌ You can't use this input for a shortcut\n" +
                                           $"`{input}`");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        var outputCommands = this._commands.Search(output);
        if (!outputCommands.IsSuccess || outputCommands.Commands.Count == 0)
        {
            if (output.Contains('.'))
            {
                response.Embed.WithDescription($"❌ No commands found for your output. Make sure you don't include the prefix (.).\n" +
                                               $"`{output}`");
            }
            else
            {
                response.Embed.WithDescription($"❌ No commands found for your output\n" +
                                               $"`{output}`");
            }

            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        return (true, null);
    }
}
