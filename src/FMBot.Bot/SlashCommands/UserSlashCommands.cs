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
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.SlashCommands;

public class UserSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;
    private readonly FriendsService _friendsService;
    private readonly IIndexService _indexService;
    private readonly UserBuilder _userBuilder;
    private readonly SettingService _settingService;
    private readonly ArtistsService _artistsService;
    private readonly OpenAiService _openAiService;

    private readonly BotSettings _botSettings;

    private InteractiveService Interactivity { get; }

    public UserSlashCommands(UserService userService,
        LastFmRepository lastFmRepository,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        IIndexService indexService,
        InteractiveService interactivity,
        FriendsService friendsService,
        UserBuilder userBuilder,
        SettingService settingService,
        ArtistsService artistsService, OpenAiService openAiService)
    {
        this._userService = userService;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._friendsService = friendsService;
        this._userBuilder = userBuilder;
        this._settingService = settingService;
        this._artistsService = artistsService;
        this._openAiService = openAiService;
        this._botSettings = botSettings.Value;
    }

    [SlashCommand("login", "Gives you a link to connect your Last.fm account to .fmbot")]
    public async Task LoginAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var token = await this._lastFmRepository.GetAuthToken();

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
                var description =
                    $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({Constants.LastFMUserUrl}{newUserSettings.UserNameLastFM})!\n\n" +
                    $"`/fmmode` has been set to: `{newUserSettings.FmEmbedType}`\n" +
                    $"`/wkmode` has been set to: `{newUserSettings.Mode ?? WhoKnowsMode.Embed}`\n" +
                    $"`/privacy` has been set to: `{newUserSettings.PrivacyLevel}`";

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
    public async Task PrivacyAsync([Summary("level", "Privacy level for your .fmbot account")] PrivacyLevel privacyLevel)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var newPrivacyLevel = await this._userService.SetPrivacyLevel(userSettings, privacyLevel);

        var reply = new StringBuilder();
        reply.AppendLine($"Your privacy level has been set to **{newPrivacyLevel}**.");
        reply.AppendLine();

        if (newPrivacyLevel == PrivacyLevel.Global)
        {
            reply.AppendLine("You will now be visible in the global WhoKnows with your Last.fm username.");
        }
        if (newPrivacyLevel == PrivacyLevel.Server)
        {
            reply.AppendLine("You will not be visible in the global WhoKnows with your Last.fm username, but users you share a server with will still see it.");
        }

        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithDescription(reply.ToString());

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);

        this.Context.LogCommandUsed();
    }

    [SlashCommand("fmmode", "Changes your '/fm' layout")]
    [UsernameSetRequired]
    public async Task ModeAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

        var response = UserBuilder.Mode(new ContextModel(this.Context, contextUser), guild);

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
            if (userSettings.FmFooterOptions.HasFlag(flag) && flag != FmFooterOption.None)
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
    public async Task WkModeAsync([Summary("mode", "Mode your fm command should use")] WhoKnowsMode mode)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var newUserSettings = await this._userService.SetWkMode(userSettings, mode);

        var reply = new StringBuilder();
        reply.Append($"Your default `WhoKnows` mode has been set to **{newUserSettings.Mode}**.");

        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithDescription(reply.ToString());

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [SlashCommand("remove", "Deletes your .fmbot account")]
    [UsernameSetRequired]
    public async Task RemoveAsync()
    {
        var userSettings = await this._userService.GetFullUserAsync(this.Context.User.Id);

        var embed = UserBuilder.GetRemoveDataEmbed(userSettings, "/");

        var builder = new ComponentBuilder()
            .WithButton("Confirm", "id");

        await RespondAsync("", new[] { embed.Build() }, components: builder.Build(), ephemeral: true);
        var msg = await this.Context.Interaction.GetOriginalResponseAsync();

        var result = await this.Interactivity.NextInteractionAsync(x => x is SocketMessageComponent c && c.Message.Id == msg.Id && x.User.Id == this.Context.User.Id,
            timeout: TimeSpan.FromSeconds(60));

        if (result.IsSuccess)
        {
            await result.Value.DeferAsync();

            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
            await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

            await this._userService.DeleteUser(userSettings.UserId);

            var followUpEmbed = new EmbedBuilder();
            followUpEmbed.WithTitle("Removal successful");
            followUpEmbed.WithDescription("Your settings, friends and any other data have been successfully deleted from .fmbot.");
            await FollowupAsync(embeds: new[] { followUpEmbed.Build() }, ephemeral: true);
        }
        else
        {
            var followUpEmbed = new EmbedBuilder();
            followUpEmbed.WithTitle("Removal timed out");
            followUpEmbed.WithDescription("If you still wish to delete your .fmbot account, please try again.");
            await FollowupAsync(embeds: new[] { followUpEmbed.Build() }, ephemeral: true);
        }

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
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
            var lfmTopArtists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, artistLimit);
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

        var commandUsesLeft = await this._openAiService.GetCommandUsesLeft(contextUser);

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
}
