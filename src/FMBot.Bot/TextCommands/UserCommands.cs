using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Selection;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("User settings")]
public class UserCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly UserBuilder _userBuilder;
    private readonly ArtistsService _artistsService;
    private readonly OpenAiService _openAiService;
    private readonly TimerService _timerService;

    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();

    public UserCommands(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        SettingService settingService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        UserBuilder userBuilder,
        InteractiveService interactivity,
        ArtistsService artistsService,
        OpenAiService openAiService,
        TimerService timerService) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._dataSourceFactory = dataSourceFactory;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this._userBuilder = userBuilder;
        this.Interactivity = interactivity;
        this._artistsService = artistsService;
        this._openAiService = openAiService;
        this._timerService = timerService;
    }

    [Command("settings", RunMode = RunMode.Async)]
    [Summary("Shows user settings for .fmbot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ServerSettings)]
    [Alias("userconfig", "usersettings", "usersetting", "setting")]
    public async Task UserSettingsAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = UserBuilder.GetUserSettings(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("profile", RunMode = RunMode.Async)]
    [Summary("Displays user stats related to Last.fm and .fmbot")]
    [UsernameSetRequired]
    [Alias("stats", "user")]
    [CommandCategories(CommandCategory.Other)]
    public async Task StatsAsync([Remainder] string userOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, contextUser, this.Context, true);

            var response =
                await this._userBuilder.ProfileAsync(new ContextModel(this.Context, prfx, contextUser), userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("link", RunMode = RunMode.Async)]
    [Summary("Links a users Last.fm profile")]
    [Alias("lastfm", "lfm")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task LinkAsync([Remainder] string userOptions = null)
    {
        var user = await this._userService.GetUserAsync(this.Context.User.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, user, this.Context, true);
            var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild?.Id);

            if (userSettings.DifferentUser && guildUsers.ContainsKey(userSettings.UserId))
            {
                await this.Context.Channel.SendMessageAsync($"<@{userSettings.DiscordUserId}>'s Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}", allowedMentions: AllowedMentions.None);
            }
            else if (userSettings.DifferentUser)
            {
                await this.Context.Channel.SendMessageAsync($"Their Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}", allowedMentions: AllowedMentions.None);
            }
            else
            {
                await this.Context.Channel.SendMessageAsync($"Your Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}", allowedMentions: AllowedMentions.None);
            }

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("judge", RunMode = RunMode.Async)]
    [Summary("Judges your music taste using AI")]
    [UsernameSetRequired]
    [Alias("roast", "compliment")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [CommandCategories(CommandCategory.Other)]
    public async Task JudgeAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, TimePeriod.Quarterly);

        var differentUserButNotAllowed = false;
        var userSettings = await this._settingService.GetUser(timeSettings.NewSearchValue, contextUser, this.Context);

        if (userSettings.DifferentUser && contextUser.UserType == UserType.User)
        {
            userSettings = await this._settingService.GetUser("", contextUser, this.Context, true);
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
            this._embed.WithDescription($"Sorry, you or the user you're searching for don't have any top artists in the selected time period.");
            this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
            await ReplyAsync(embed: this._embed.Build());
            return;
        }

        topArtists = topArtists.Take(artistLimit).ToList();

        var commandUsesLeft = await this._openAiService.GetJudgeUsesLeft(contextUser);

        try
        {
            var response =
                UserBuilder.JudgeAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, timeSettings, contextUser.UserType, commandUsesLeft, differentUserButNotAllowed);

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

            var result = await this.Interactivity.SendSelectionAsync(selection, this.Context.Channel, TimeSpan.FromMinutes(10));

            var handledResponse = await this._userBuilder.JudgeHandleAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, result, topArtists);

            this.Context.LogCommandUsed(handledResponse.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("userreactions", RunMode = RunMode.Async)]
    [Summary("Sets the automatic emoji reactions for the `fm` and `featured` command.\n\n" +
             "Use this command without any emojis to disable.")]
    [Examples("userreactions :PagChomp: :PensiveBlob:", "userreactions 😀 😯 🥵", "userreactions 😀 😯 :PensiveBlob:", "userreactions")]
    [Alias("usersetreactions", "useremojis", "userreacts")]
    [UsernameSetRequired]
    public async Task SetUserReactionsAsync([Remainder] string emojis = null)
    {
        var user = await this._userService.GetUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var supporterRequiredResponse = UserBuilder.UserReactionsSupporterRequired(new ContextModel(this.Context, prfx, user), prfx);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        if (string.IsNullOrWhiteSpace(emojis))
        {
            await this._userService.SetUserReactionsAsync(user.UserId, null);

            if (user?.EmoteReactions == null || !user.EmoteReactions.Any())
            {
                this._embed.WithDescription("Use this command with emojis to set the default reactions to `fm` and `featured`.\n\n" +
                                            "For example:\n" +
                                            $"`{prfx}userreactions ⬆️ ⬇️`");
            }
            else
            {
                this._embed.WithDescription("Removed all user reactions!");
            }

            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            await ReplyAsync(embed: this._embed.Build());

            this.Context.LogCommandUsed();

            return;
        }

        emojis = emojis.Replace("><", "> <");
        var emoteArray = emojis.Split(" ");

        if (emoteArray.Count() > 5)
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, you can't set more then 5 emoji reacts. Please try again.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        if (!GuildService.ValidateReactions(emoteArray))
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                                        "Please check if you have a space between every emoji.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        await this._userService.SetUserReactionsAsync(user.UserId, emoteArray);

        this._embed.WithTitle("Automatic user emoji reactions set");
        this._embed.WithDescription("This will apply these emotes to all your `fm` and `featured` commands, regardless of server. " +
                                    "Please check if all reactions have been applied to this message correctly.");
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        var message = await ReplyAsync(embed: this._embed.Build());
        this.Context.LogCommandUsed();

        try
        {
            await GuildService.AddReactionsAsync(message, emoteArray);
        }
        catch (Exception e)
        {
            this._embed.WithTitle("Error in set emoji reactions");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);

            if (e.Message.ToLower().Contains("permission"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "The bot does not have the `Add Reactions` permission in this server. Make sure that the permissions for the bot and channel are set correctly.");
            }
            else if (e.Message.ToLower().Contains("unknown emoji"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "One or more of the emojis you used are from a server that doesn't have .fmbot. Make sure you only use emojis from servers that have .fmbot.");
            }
            else
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "Make sure the permissions are set correctly and the emojis are from a server that .fmbot is in.");
            }

            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Error);
        }
    }

    [Command("featured", RunMode = RunMode.Async)]
    [Summary("Displays the currently picked feature and the user.\n\n" +
             "This command will also show something special if the user is in your server")]
    [Alias("featuredavatar", "featureduser", "featuredalbum", "avatar", "ftrd", "ftd", "feat")]
    [CommandCategories(CommandCategory.Other)]
    public async Task FeaturedAsync([Remainder] string options = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = await this._userBuilder.FeaturedAsync(new ContextModel(this.Context, prfx, contextUser));

            IUserMessage message;
            if (response.ResponseType == ResponseType.Embed)
            {
                message = await ReplyAsync("", false, response.Embed.Build());
            }
            else
            {
                message = await ReplyAsync(response.Text);
            }

            if (message != null && response.CommandResponse == CommandResponse.Ok)
            {
                PublicProperties.UsedCommandsResponseMessageId.TryAdd(this.Context.Message.Id, message.Id);
                PublicProperties.UsedCommandsResponseContextId.TryAdd(message.Id, this.Context.Message.Id);

                if (this._timerService.CurrentFeatured?.Reactions != null && this._timerService.CurrentFeatured.Reactions.Any())
                {
                    await GuildService.AddReactionsAsync(message, this._timerService.CurrentFeatured.Reactions);
                }
                else
                {
                    if (contextUser.EmoteReactions != null && contextUser.EmoteReactions.Any() && SupporterService.IsSupporter(contextUser.UserType))
                    {
                        await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
                    }
                    else if (this.Context.Guild != null)
                    {
                        await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild, response.Text == "in-server");
                    }
                }
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, sendReply: false);
            await ReplyAsync(
                "Unable to show the featured avatar on FMBot due to an internal error. \n" +
                "The bot might not have changed its avatar since its last startup. Please wait until a new featured user is chosen.");
        }
    }

    [Command("featuredlog", RunMode = RunMode.Async)]
    [Summary("Shows featured history")]
    [Alias("featuredhistory", "recentfeatured", "rf", "recentlyfeatured", "fl", "flog")]
    [Options("global/server/friends/self")]
    [CommandCategories(CommandCategory.Other)]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync([Remainder] string options = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var view = SettingService.SetFeaturedTypeView(userSettings.NewSearchValue);

            if (view != FeaturedView.User)
            {
                userSettings = await this._settingService.GetUser("", contextUser, this.Context);
            }

            var response =
                await this._userBuilder.FeaturedLogAsync(new ContextModel(this.Context, prfx, contextUser),
                    userSettings, view);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("rateyourmusic", RunMode = RunMode.Async)]
    [Summary("Enables or disables the rateyourmusic links. This changes all album links in .fmbot to RYM links instead of Last.fm links.")]
    [Alias("rym")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    public async Task RateYourMusicAsync([Remainder] string options = null)
    {
        try
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newRymSetting = await this._userService.ToggleRymAsync(user);

            if (newRymSetting == true)
            {
                this._embed.WithDescription(
                    "**RateYourMusic** integration has now been enabled for your account.\n\n" +
                    "All album links should now link to RateYourMusic. Please note that since RYM does not provide an API you will be linked to their search page.");
            }
            else
            {
                this._embed.WithDescription(
                    "**RateYourMusic** integration has now been disabled.\n\n" +
                    "All available album links should now link to Last.fm.\n" +
                    "To re-enable the integration, simply do this command again.");
            }

            this._embed.WithColor(32, 62, 121);
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botscrobbling", RunMode = RunMode.Async)]
    [Summary("Enables or disables the bot scrobbling. For more info, use the command.")]
    [Alias("botscrobble", "bottrack", "bottracking")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    public async Task BotTrackingAsync([Remainder] string option = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response =
                UserBuilder.BotScrobblingAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("fmmode", RunMode = RunMode.Async)]
    [Summary("Sends you a dm so you can configure your `fm` command.\n\n" +
             "Servers can override your mode with `{{prfx}}servermode`.")]
    [Examples("mode")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task ModeAsync(params string[] otherSettings)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = UserBuilder.FmMode(new ContextModel(this.Context, prfx, contextUser), guild);

            await this.Context.User.SendMessageAsync(embed: response.Embed.Build(),
                components: response.Components.Build());

            if (this.Context.Guild != null)
            {
                await ReplyAsync("Check your DMs to configure your `fm` settings!");
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await ReplyAsync("Error occurred while trying to send DM, maybe you have DMs disabled. \n" +
                             "Try using the slash command version `/fmmode` instead.");
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    [Command("responsemode", RunMode = RunMode.Async)]
    [Summary("Change how your whoknows and top list commands look.")]
    [Examples("responsemode")]
    [UsernameSetRequired]
    [Alias("wkmode", "topmode", "toplistmode")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task ResponseModeAsync(params string[] otherSettings)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.ResponseMode(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("mode", RunMode = RunMode.Async)]
    [UsernameSetRequired]
    [ExcludeFromHelp]
    [Alias("md")]
    public async Task PickModeAsync(params string[] otherSettings)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.ModePick(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("privacy", RunMode = RunMode.Async)]
    [Summary("Changes your visibility to other .fmbot users.\n\n" +
             "The default privacy setting is 'Server'.")]
    [Options("**Global**: You are visible in the global WhoKnows with your Last.fm username",
        "**Server**: You are not visible in global WhoKnows, but users in the same server will still see your name.")]
    [Examples("privacy")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task PrivacyAsync([Remainder] string _ = null)
    {
        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.Privacy(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("login", RunMode = RunMode.Async)]
    [Summary("Logs you in using a link.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [Alias("set", "setusername", "fm set", "connect")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task LoginAsync([Remainder] string _ = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var msg = this.Context.Message as SocketUserMessage;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddMinutes(1) >= DateTimeOffset.Now)
            {
                var loginCommand = PublicProperties.SlashCommands.ContainsKey("login") ? $"</login:{PublicProperties.SlashCommands["login"]}>" : "`/login`";

                await ReplyAsync($"A login link has already been sent to your DMs.\n" +
                                 $"Didn't receive a link? Please check if you have DMs enabled for this server and try again.\n" +
                                 $"You can also try using the slash command version {loginCommand}.");

                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now.AddMinutes(-5);
                return;
            }

            StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
        }
        else
        {
            StackCooldownTarget.Add(msg.Author);
            StackCooldownTimer.Add(DateTimeOffset.Now);
        }

        var existingUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var token = await this._dataSourceFactory.GetAuthToken();

        var reply = new StringBuilder();
        var link =
            $"http://www.last.fm/api/auth/?api_key={this._botSettings.LastFm.PublicKey}&token={token.Content.Token}";

        if (existingUserSettings == null)
        {
            reply.AppendLine($"**[Click here to add your Last.fm account to .fmbot]({link})**");
            reply.AppendLine();
            reply.AppendLine("Link will expire after 5 minutes, please wait a moment after allowing access...");
            reply.AppendLine();
            reply.AppendLine("Don't have a Last.fm account yet? " +
                             $"[Sign up here](https://last.fm/join) and see [how to track your music here](https://last.fm/about/trackmymusic). " +
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

        this._embed.WithDescription(reply.ToString());
        this._embed.WithColor(DiscordConstants.LastFmColorRed);

        var authorizeMessage = await this.Context.User.SendMessageAsync("", false, this._embed.Build());

        if (!this._guildService.CheckIfDM(this.Context))
        {
            var serverEmbed = new EmbedBuilder()
                .WithColor(DiscordConstants.InformationColorBlue);

            var guildReply = "Check your DMs for a link to connect your Last.fm account to .fmbot!";

            if (this.Context.Message.Content.Contains("set"))
            {
                guildReply += $"\nPlease use `{prfx}mode` to change how your .fm command looks.";
            }

            if (existingUserSettings != null)
            {
                guildReply = $"You have already logged in before, however a link to re-connect your Last.fm account to .fmbot has still been sent to your DMs!\n\n" +
                             $"Using Spotify and having problems with your music not being tracked or it lagging behind? Re-logging in again will not fix this, please use `{prfx}outofsync` for help instead.";
            }

            serverEmbed.WithDescription(guildReply);
            await this.Context.Channel.SendMessageAsync("", false, serverEmbed.Build());
        }

        var success = await this._userService.GetAndStoreAuthSession(this.Context.User, token.Content.Token);

        if (success)
        {
            var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            await authorizeMessage.ModifyAsync(m =>
            {
                var settingCommand = PublicProperties.SlashCommands.ContainsKey("settings") ? $"</settings:{PublicProperties.SlashCommands["settings"]}>" : "`/settings`";
                var description =
                    $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(newUserSettings.UserNameLastFM)})!\n\n" +
                    $"Use {settingCommand} to change your settings and to customize your .fmbot experience.";

                var sourceGuildId = this.Context.Guild?.Id;
                var sourceChannel = this.Context.Channel;

                if (sourceGuildId != null && sourceChannel != null)
                {
                    description += "\n\n" +
                                   $"**[Click here to go back to #{sourceChannel.Name}](https://discord.com/channels/{sourceGuildId}/{sourceChannel.Id}/)**";
                }

                m.Embed = new EmbedBuilder()
                    .WithDescription(description)
                    .WithColor(DiscordConstants.SuccessColorGreen)
                    .Build();
            });

            this.Context.LogCommandUsed();

            if (existingUserSettings != null && !string.Equals(existingUserSettings.UserNameLastFM, newUserSettings.UserNameLastFM, StringComparison.CurrentCultureIgnoreCase))
            {
                await this._indexService.IndexUser(newUserSettings);
            }

            if (!this._guildService.CheckIfDM(this.Context))
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
            await authorizeMessage.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription($"Login expired. Re-run the command to try again.\n\n" +
                                     $"Getting 'Invalid API key' error? This is a [known Last.fm issue](https://support.last.fm/t/invalid-api-key-error-when-connecting-to-discord-fmbot-on-iphone/65329) on iOS. " +
                                     $"Current workaround is to try connecting on a different device.\n\n" +
                                     $"Still having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.")
                    .WithColor(DiscordConstants.WarningColorOrange)
                    .Build();
            });

            this.Context.LogCommandUsed(CommandResponse.WrongInput);
        }
    }

    [Command("remove", RunMode = RunMode.Async)]
    [Summary("Deletes your .fmbot account")]
    [Alias("delete", "removedata", "deletedata", "logout")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task RemoveAsync([Remainder] string confirmation = null)
    {
        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser == null)
        {
            await ReplyAsync("Sorry, but we don't have any data from you in our database.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
