using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Selection;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.SlashCommands;

public class UserSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GuildService _guildService;
    private readonly FriendsService _friendsService;
    private readonly IIndexService _indexService;
    private readonly UserBuilder _userBuilder;
    private readonly SettingService _settingService;
    private readonly ArtistsService _artistsService;
    private readonly OpenAiService _openAiService;
    private readonly ImportService _importService;
    private readonly IPrefixService _prefixService;

    private readonly BotSettings _botSettings;

    private InteractiveService Interactivity { get; }

    public UserSlashCommands(UserService userService,
        IDataSourceFactory dataSourceFactory,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        IIndexService indexService,
        InteractiveService interactivity,
        FriendsService friendsService,
        UserBuilder userBuilder,
        SettingService settingService,
        ArtistsService artistsService,
        OpenAiService openAiService,
        ImportService importService,
        IPrefixService prefixService)
    {
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
        this._guildService = guildService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._friendsService = friendsService;
        this._userBuilder = userBuilder;
        this._settingService = settingService;
        this._artistsService = artistsService;
        this._openAiService = openAiService;
        this._importService = importService;
        this._prefixService = prefixService;
        this._botSettings = botSettings.Value;
    }

    [SlashCommand("settings", "Shows user settings for .fmbot")]
    [UsernameSetRequired]
    public async Task UserSettingsAsync()
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = await this._userBuilder.GetUserSettings(new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.UserSetting)]
    [UsernameSetRequired]
    public async Task GetUserSetting(string[] inputs)
    {
        var setting = inputs.First().Replace("us-", "");

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out UserSetting userSetting))
        {
            ResponseModel response;
            switch (userSetting)
            {
                case UserSetting.Privacy:
                    {
                        response = UserBuilder.Privacy(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.FmMode:
                    {
                        response = UserBuilder.FmMode(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.WkMode:
                    {
                        response = UserBuilder.WkMode(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.BotScrobbling:
                    {
                        response = await this._userBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.SpotifyImport:
                    {
                        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

                        if (supporterRequired != null)
                        {
                            await this.Context.SendResponse(this.Interactivity, supporterRequired, ephemeral: true);
                            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                            return;
                        }

                        var hasImported = await this._importService.HasImported(contextUser.UserId);

                        response = UserBuilder.ImportMode(new ContextModel(this.Context, contextUser), hasImported);

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.UserReactions:
                    {
                        var supporterRequired = UserBuilder.UserReactionsSupporterRequired(new ContextModel(this.Context, contextUser), prfx);

                        if (supporterRequired != null)
                        {
                            await this.Context.SendResponse(this.Interactivity, supporterRequired, ephemeral: true);
                            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                            return;
                        }

                        response = UserBuilder.UserReactionsAsync(new ContextModel(this.Context, contextUser), prfx);

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.OutOfSync:
                    {
                        response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                case UserSetting.DeleteAccount:
                    {
                        response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [SlashCommand("login", "Gives you a link to connect your Last.fm account to .fmbot")]
    public async Task LoginAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var token = await this._dataSourceFactory.GetAuthToken();

        try
        {
            var reply = new StringBuilder();
            var link =
                $"http://www.last.fm/api/auth/?api_key={this._botSettings.LastFm.PublicKey}&token={token.Content.Token}";

            if (contextUser == null)
            {
                reply.AppendLine($"**[Click here to add your Last.fm account to .fmbot]({link})**");
                reply.AppendLine();
                reply.AppendLine("Link will expire after 5 minutes, please wait a moment after allowing access...");
                reply.AppendLine();
                reply.AppendLine("Don't have a Last.fm account yet? " +
                                 "[Sign up here](https://last.fm/join) and see [how to track your music here](https://last.fm/about/trackmymusic). +" +
                                 $"After that you can [authorize .fmbot]({link}).");
            }
            else
            {
                reply.AppendLine(
                    $"You have already logged in before. If you want to change or reconnect your connected Last.fm account, **[click here.]({link})** " +
                    $"Note that this link will expire after 5 minutes. Also use this link if the bot says you have to re-login.");
                reply.AppendLine();
                reply.AppendLine(
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? " +
                    $"Re-logging in again will not fix this, please use `/outofsync` for help instead.");
            }

            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.LastFmColorRed);
            embed.WithDescription(reply.ToString());

            await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
            this.Context.LogCommandUsed();

            var loginSuccess = await this._userService.GetAndStoreAuthSession(this.Context.User, token.Content.Token);

            var followUpEmbed = new EmbedBuilder();
            if (loginSuccess)
            {
                followUpEmbed.WithColor(DiscordConstants.SuccessColorGreen);
                var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
                var settingCommand = PublicProperties.SlashCommands.ContainsKey("settings") ? $"</settings:{PublicProperties.SlashCommands["settings"]}>" : "`/settings`";
                var description =
                    $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(newUserSettings.UserNameLastFM)})!\n\n" +
                    $"Use {settingCommand} to change your settings and to customize your .fmbot experience.";

                followUpEmbed.WithDescription(description);

                await FollowupAsync(null, new[] { followUpEmbed.Build() }, ephemeral: true);

                this.Context.LogCommandUsed();

                if (contextUser != null && !string.Equals(contextUser.UserNameLastFM, newUserSettings.UserNameLastFM, StringComparison.CurrentCultureIgnoreCase))
                {
                    await this._indexService.IndexUser(newUserSettings);
                }

                if (this.Context.Guild != null)
                {
                    var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);
                    if (guild != null)
                    {
                        var discordGuildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                        var newGuildUser = new GuildUser
                        {
                            Bot = false,
                            GuildId = guild.GuildId,
                            UserId = newUserSettings.UserId,
                            UserName = discordGuildUser?.DisplayName,
                        };

                        if (guild.WhoKnowsWhitelistRoleId.HasValue && discordGuildUser != null)
                        {
                            newGuildUser.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                        }

                        await this._indexService.AddGuildUserToDatabase(newGuildUser);
                    }
                }
            }
            else
            {
                followUpEmbed.WithColor(DiscordConstants.WarningColorOrange);
                followUpEmbed.WithDescription(
                    $"Login expired. Re-run the command to try again.\n\n" +
                    $"Having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.");

                await FollowupAsync(null, new[] { followUpEmbed.Build() }, ephemeral: true);

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [SlashCommand("privacy", "Changes your visibility to other .fmbot users in Global WhoKnows")]
    [UsernameSetRequired]
    public async Task PrivacyAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = UserBuilder.Privacy(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.FmPrivacySetting)]
    [UsernameSetRequired]
    public async Task SetPrivacy(string[] inputs)
    {
        var embed = new EmbedBuilder();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out PrivacyLevel privacyLevel))
        {
            var newPrivacyLevel = await this._userService.SetPrivacyLevel(userSettings.UserId, privacyLevel);

            embed.WithDescription($"Your privacy level has been set to **{newPrivacyLevel}**.");
            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }

    [SlashCommand("fmmode", "Changes your '/fm' layout")]
    [UsernameSetRequired]
    public async Task ModeAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

        var response = UserBuilder.FmMode(new ContextModel(this.Context, contextUser), guild);

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.FmSettingType)]
    [UsernameSetRequired]
    public async Task SetEmbedType(string[] inputs)
    {
        var embed = new EmbedBuilder();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            var newUserSettings = await this._userService.SetSettings(userSettings, embedType, FmCountType.None);

            embed.WithDescription($"Your `fm` mode has been set to **{newUserSettings.FmEmbedType}**.");
            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
    }

    [ComponentInteraction(InteractionConstants.FmSettingFooter)]
    [UsernameSetRequired]
    public async Task SetFooterOptions(string[] inputs)
    {
        var embed = new EmbedBuilder();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var maxOptions = userSettings.UserType == UserType.User ? Constants.MaxFooterOptions : Constants.MaxFooterOptionsSupporter;
        var amountSelected = 0;

        foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
        {
            if (Enum.TryParse(option, out FmFooterOption flag))
            {
                var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                if (!supporterOnly)
                {
                    if (inputs.Any(a => a == option) && amountSelected <= maxOptions)
                    {
                        userSettings.FmFooterOptions |= flag;
                        amountSelected++;
                    }
                    else
                    {
                        userSettings.FmFooterOptions &= ~flag;
                    }
                }
            }
        }

        await SaveFooterOptions(userSettings, embed);
    }

    [ComponentInteraction(InteractionConstants.FmSettingFooterSupporter)]
    public async Task SetSupporterFooterOptions(string[] inputs)
    {
        var embed = new EmbedBuilder();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (userSettings.UserType == UserType.User)
        {
            return;
        }

        var maxOptions = userSettings.UserType == UserType.User ? 0 : 1;
        var amountSelected = 0;

        foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
        {
            if (Enum.TryParse(option, out FmFooterOption flag))
            {
                var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                if (supporterOnly)
                {
                    if (inputs.Any(a => a == option) && amountSelected <= maxOptions && option != "none")
                    {
                        userSettings.FmFooterOptions |= flag;
                        amountSelected++;
                    }
                    else
                    {
                        userSettings.FmFooterOptions &= ~flag;
                    }
                }
            }
        }

        await SaveFooterOptions(userSettings, embed);
    }

    private async Task SaveFooterOptions(User userSettings, EmbedBuilder embed)
    {
        userSettings = await this._userService.SetFooterOptions(userSettings, userSettings.FmFooterOptions);

        var description = new StringBuilder();
        description.AppendLine("Your `fm` footer options have been set to:");

        foreach (var flag in userSettings.FmFooterOptions.GetUniqueFlags())
        {
            if (userSettings.FmFooterOptions.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("wkmode", "Changes your default whoknows mode")]
    [UsernameSetRequired]
    public async Task WkModeAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = UserBuilder.WkMode(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.WkModeSetting)]
    [UsernameSetRequired]
    public async Task SetWkModeAsync(string[] inputs)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out WhoKnowsMode mode))
        {
            var newUserSettings = await this._userService.SetWkMode(userSettings, mode);

            var reply = new StringBuilder();
            reply.Append($"Your default `WhoKnows` mode has been set to **{newUserSettings.Mode}**.");

            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.InformationColorBlue);
            embed.WithDescription(reply.ToString());

            await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
            this.Context.LogCommandUsed();
        }
    }

    [SlashCommand("remove", "Deletes your .fmbot account")]
    [UsernameSetRequired]
    public async Task RemoveAsync()
    {
        var userSettings = await this._userService.GetFullUserAsync(this.Context.User.Id);

        var response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, userSettings));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction($"{InteractionConstants.RemoveFmbotAccount}-*")]
    [UsernameSetRequired]
    public async Task RemoveAccountModal(string discordUserId)
    {
        var parsedId = ulong.Parse(discordUserId);
        if (parsedId != this.Context.User.Id)
        {
            await RespondAsync("Hey, this button is not for you. At least you tried.", ephemeral: true);
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<RemoveAccountConfirmModal>($"{InteractionConstants.RemoveFmbotAccountModal}-{discordUserId}");
    }

    [ModalInteraction($"{InteractionConstants.RemoveFmbotAccountModal}-*")]
    [UsernameSetRequired]
    public async Task RemoveConfirmAsync(string discordUserId, RemoveAccountConfirmModal modal)
    {
        var parsedId = ulong.Parse(discordUserId);
        if (parsedId != this.Context.User.Id)
        {
            await RespondAsync("Hey, this button is not for you. At least you tried.", ephemeral: true);
            return;
        }

        if (modal.Confirmation?.ToLower() != "confirm")
        {
            await RespondAsync("Account deletion cancelled, wrong modal input", ephemeral: true);
            return;
        }

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (userSettings == null)
        {
            await RespondAsync("We don't have any data from you in our database", ephemeral: true);
            return;
        }

        await this.DeferAsync(true);

        await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
        await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

        await this._userService.DeleteUser(userSettings.UserId);

        var followUpEmbed = new EmbedBuilder();
        followUpEmbed.WithTitle("Removal successful");
        followUpEmbed.WithDescription("Your settings, friends and any other data have been successfully deleted from .fmbot.");
        await FollowupAsync(embeds: new[] { followUpEmbed.Build() }, ephemeral: true);
    }


    [SlashCommand("judge", "Judges your music taste using AI")]
    [UsernameSetRequired]
    public async Task JudgeAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("User", "The user to judge (Supporter-only option)")] string user = null)
    {
        var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
        var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime);

        var differentUserButNotAllowed = false;

        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        if (userSettings.DifferentUser && contextUser.UserType == UserType.User)
        {
            userSettings = await this._settingService.GetUser("", contextUser, this.Context.Guild, this.Context.User, true);

            differentUserButNotAllowed = true;
        }

        List<string> topArtists;
        const int artistLimit = 15;
        if (timeSettings.TimePeriod == TimePeriod.Quarterly && !userSettings.DifferentUser)
        {
            topArtists = await this._artistsService.GetRecentTopArtists(userSettings.DiscordUserId, daysToGoBack: 90);
        }
        else
        {
            var lfmTopArtists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, artistLimit);
            topArtists = lfmTopArtists.Content?.TopArtists?.Select(s => s.ArtistName).ToList();
        }

        if (topArtists == null || !topArtists.Any())
        {
            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.LastFmColorRed);
            embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top artists in the selected time period.");
            this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
            await RespondAsync(embed: embed.Build());
            return;
        }

        topArtists = topArtists.Take(artistLimit).ToList();

        var commandUsesLeft = await this._openAiService.GetJudgeUsesLeft(contextUser);

        try
        {
            var response =
                await this._userBuilder.JudgeAsync(new ContextModel(this.Context, contextUser), userSettings, timeSettings, contextUser.UserType, commandUsesLeft, differentUserButNotAllowed);

            if (commandUsesLeft <= 0)
            {
                await this.Context.SendResponse(this.Interactivity, response);
                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                return;
            }

            var pageBuilder = new PageBuilder()
                .WithDescription(response.Embed.Description)
                .WithFooter(response.Embed.Footer)
                .WithColor(DiscordConstants.InformationColorBlue);

            var items = new Item[]
            {
                new("Compliment", new Emoji("🙂")),
                new("Roast", new Emoji("🔥")),
            };

            var selection = new SelectionBuilder<Item>()
                .WithOptions(items)
                .WithStringConverter(item => item.Name)
                .WithEmoteConverter(item => item.Emote)
                .WithSelectionPage(pageBuilder)
                .AddUser(this.Context.User)
                .Build();

            var result = await this.Interactivity.SendSelectionAsync(selection, this.Context.Interaction, TimeSpan.FromMinutes(10));

            var handledResponse = await this._userBuilder.JudgeHandleAsync(new ContextModel(this.Context, contextUser),
                userSettings, result, topArtists);

            this.Context.LogCommandUsed(handledResponse.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("featured", "Shows what is currently featured (and the bots avatar)")]
    [UsernameSetRequired]
    public async Task FeaturedAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._userBuilder.FeaturedAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);

        var message = await this.Context.Interaction.GetOriginalResponseAsync();

        if (message != null && response.CommandResponse == CommandResponse.Ok)
        {
            if (contextUser.EmoteReactions != null && contextUser.EmoteReactions.Any())
            {
                await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
            }
            else if (this.Context.Guild != null)
            {
                await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild, response.Text == "in-server");
            }
        }
    }

    [SlashCommand("botscrobbling", "Shows info about music bot scrobbling and allows you to change your settings")]
    [UsernameSetRequired]
    public async Task BotScrobblingAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._userBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.BotScrobblingEnable)]
    [UserSessionRequired]
    public async Task EnableBotScrobbling()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var reply = new StringBuilder();

        if (contextUser.MusicBotTrackingDisabled != true)
        {
            reply.AppendLine("✅ Music bot scrobbling for your account is already enabled.");
        }
        else
        {
            await this._userService.ToggleBotScrobblingAsync(contextUser.UserId, false);
            reply.AppendLine("✅ Enabled music bot scrobbling for your account.");
        }

        var embed = new EmbedBuilder();
        embed.WithDescription(reply.ToString());
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.BotScrobblingDisable)]
    [UserSessionRequired]
    public async Task DisableBotScrobbling()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var reply = new StringBuilder();

        if (contextUser.MusicBotTrackingDisabled == true)
        {
            reply.AppendLine("❌ Music bot scrobbling for your account is already disabled.");
        }
        else
        {
            await this._userService.ToggleBotScrobblingAsync(contextUser.UserId, true);
            reply.AppendLine("❌ Disabled music bot scrobbling for your account.");
        }

        var embed = new EmbedBuilder();
        embed.WithDescription(reply.ToString());
        embed.WithColor(DiscordConstants.LastFmColorRed);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [SlashCommand("featuredlog", "Shows you or someone else their featured history")]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync(
        [Summary("View", "Type of log you want to view")] FeaturedView view = FeaturedView.User,
        [Summary("User", "The user to view the featured log for (defaults to self)")] string user = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._userBuilder.FeaturedLogAsync(new ContextModel(this.Context, contextUser), userSettings, view);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("stats", "Shows you or someone else their profile")]
    [UsernameSetRequired]
    public async Task StatsAsync(
        [Summary("User", "The user of which you want to view their profile")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._userBuilder.StatsAsync(new ContextModel(this.Context, contextUser), userSettings, contextUser);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.ImportSetting)]
    [UsernameSetRequired]
    public async Task SetImport(string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out DataSource dataSource))
        {
            var newUserSettings = await this._userService.SetDataSource(contextUser, dataSource);

            var name = newUserSettings.DataSource.GetAttribute<OptionAttribute>().Name;

            var embed = new EmbedBuilder();
            embed.WithDescription($"Import mode set to **{name}**.\n\n" +
                                  $"Your stored top artist/albums/tracks are being recalculated. \n\n" +
                                  $"**Please wait for this to be confirmed before switching modes again.**");
            embed.WithColor(DiscordConstants.SuccessColorGreen);

            ComponentBuilder components = null;
            if (dataSource == DataSource.LastFm)
            {
                components = new ComponentBuilder()
                    .WithButton("Also delete all imported plays", InteractionConstants.ImportClear, style: ButtonStyle.Danger);
            }

            await RespondAsync(null, new[] { embed.Build() }, ephemeral: true, components: components?.Build());
            this.Context.LogCommandUsed();

            await this._indexService.RecalculateTopLists(newUserSettings);

            embed.WithDescription("✅ Your stored top artist/albums/tracks have successfully been recalculated.");
            await FollowupAsync(null, new[] { embed.Build() }, ephemeral: true, components: components?.Build());
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClear)]
    [UsernameSetRequired]
    public async Task ClearImports()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        await this._importService.RemoveImportPlays(contextUser);

        var embed = new EmbedBuilder();
        embed.WithDescription($"All your imported plays have been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportManage)]
    [UsernameSetRequired]
    public async Task ImportManage()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.UserType != UserType.Admin && contextUser.UserType != UserType.Owner)
        {
            await RespondAsync("Not available yet!");
            return;
        }

        try
        {
            var hasImported = await this._importService.HasImported(contextUser.UserId);
            var response = UserBuilder.ImportMode(new ContextModel(this.Context, contextUser), hasImported);

            await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
