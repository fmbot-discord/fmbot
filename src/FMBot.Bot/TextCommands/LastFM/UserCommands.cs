using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Extensions;
using Fergun.Interactive.Selection;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("User settings")]
public class UserCommands : BaseCommandModule
{
    private readonly CrownService _crownService;
    private readonly FriendsService _friendsService;
    private readonly FeaturedService _featuredService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly SettingService _settingService;
    private readonly TimerService _timer;
    private readonly UserService _userService;
    public readonly UserBuilder _userBuilder;
    private readonly ArtistsService _artistsService;
    private readonly PlayService _playService;
    private readonly TimeService _timeService;
    private readonly CommandService _commands;


    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();

    public UserCommands(
        FriendsService friendsService,
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        LastFmRepository lastFmRepository,
        SettingService settingService,
        TimerService timer,
        UserService userService,
        CrownService crownService,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        UserBuilder userBuilder,
        InteractiveService interactivity, ArtistsService artistsService, PlayService playService, TimeService timeService, CommandService commands) : base(botSettings)
    {
        this._friendsService = friendsService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._timer = timer;
        this._userService = userService;
        this._crownService = crownService;
        this._featuredService = featuredService;
        this._userBuilder = userBuilder;
        this.Interactivity = interactivity;
        this._artistsService = artistsService;
        this._playService = playService;
        this._timeService = timeService;
        this._commands = commands;
    }

    [Command("stats", RunMode = RunMode.Async)]
    [Summary("Displays user stats related to Last.fm and .fmbot")]
    [UsernameSetRequired]
    [Alias("profile", "user")]
    [CommandCategories(CommandCategory.Other)]
    public async Task StatsAsync([Remainder] string userOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var user = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, user, this.Context);

            var response =
                await this._userBuilder.StatsAsync(new ContextModel(this.Context, prfx, user), userSettings, user);

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
        var user = await this._userService.GetFullUserAsync(this.Context.User.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, user, this.Context, true);

            if (userSettings.DifferentUser)
            {
                await this.Context.Channel.SendMessageAsync($"<@{userSettings.DiscordUserId}>'s Last.fm profile: {Constants.LastFMUserUrl}{userSettings.UserNameLastFm}", allowedMentions: AllowedMentions.None);
            }
            else
            {
                await this.Context.Channel.SendMessageAsync($"Your Last.fm profile: {Constants.LastFMUserUrl}{userSettings.UserNameLastFm}", allowedMentions: AllowedMentions.None);
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
    [CommandCategories(CommandCategory.Other)]
    [ExcludeFromHelp]
    public async Task JudgeAsync([Remainder] string userOptions = null)
    {
        var user = await this._userService.GetUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (user.UserType != UserType.Admin && user.UserType != UserType.Owner)
        {
            await ReplyAsync("Nothing to see here!");
        }

        var topArtists = await this._artistsService.GetRecentTopArtists(this.Context.User.Id, daysToGoBack: 60);

        try
        {
            var response =
                await this._userBuilder.JudgeAsync(new ContextModel(this.Context, prfx, user));

            var pageBuilder = new PageBuilder()
                .WithDescription(response.Embed.Description)
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
                .Build();

            var result = await this.Interactivity.SendSelectionAsync(selection, this.Context.Channel, TimeSpan.FromMinutes(10));

            var selected = result.Value.Name;

            if (selected == "Compliment")
            {
                var embed = new EmbedBuilder()
                    .WithDescription("<a:loading:821676038102056991> Loading your compliment...")
                    .WithColor(new Color(186, 237, 169));

                await result.Message.ModifyAsync(x =>
                {
                    x.Embed = embed.Build();
                    x.Components = new ComponentBuilder().Build();
                });

                var complimentResponse = await this._userBuilder.JudgeComplimentAsync(new ContextModel(this.Context, prfx, user), topArtists);

                await result.Message.ModifyAsync(x =>
                {
                    x.Embed = complimentResponse.Embed.Build();
                    x.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithDescription("<a:loading:821676038102056991> Loading your roast (don't take it personally)...")
                    .WithColor(new Color(255, 122, 1));

                await result.Message.ModifyAsync(x =>
                {
                    x.Embed = embed.Build();
                    x.Components = new ComponentBuilder().Build();
                });

                var complimentResponse = await this._userBuilder.JudgeRoastAsync(new ContextModel(this.Context, prfx, user), topArtists);

                await result.Message.ModifyAsync(x =>
                {
                    x.Embed = complimentResponse.Embed.Build();
                    x.Components = new ComponentBuilder().Build();
                });
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private sealed record Item(string Name, IEmote Emote);

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
            var response = await this._userBuilder.FeaturedAsync(new ContextModel(this.Context, prfx));

            IUserMessage message;
            if (response.ResponseType == ResponseType.Embed)
            {
                message = await ReplyAsync("", false, response.Embed.Build());
            }
            else
            {
                message = await ReplyAsync(response.Text);
            }

            if (message != null && this.Context.Guild != null && response.CommandResponse == CommandResponse.Ok)
            {
                await this._guildService.AddReactionsAsync(message, this.Context.Guild, response.Text == "in-server");
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
    [Summary("Shows you or someone else their featured history")]
    [Alias("featuredhistory", "recentfeatured", "rf", "recentlyfeatured", "fl")]
    [CommandCategories(CommandCategory.Other)]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync([Remainder] string options = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var response =
                await this._userBuilder.FeaturedLogAsync(new ContextModel(this.Context, prfx, contextUser),
                    userSettings);

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
                await this._userBuilder.BotScrobblingAsync(new ContextModel(this.Context, prfx, contextUser), option);

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
    [Alias("md", "mode")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task ModeAsync(params string[] otherSettings)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = UserBuilder.Mode(new ContextModel(this.Context, prfx, contextUser), guild);

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

    [Command("wkmode", RunMode = RunMode.Async)]
    [Summary("Change how your whoknows commands look.")]
    [Options("Modes: `embed` or `image`")]
    [Examples("wkmode embed", "wkmode image")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task WkModeAsync(params string[] otherSettings)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var newUserSettings = UserService.SetWkMode(userSettings, otherSettings);

        await this._userService.SetLastFm(this.Context.User, newUserSettings);

        var setReply = $"Your default `WhoKnows` mode has been set to **{newUserSettings.Mode}**.";

        this._embed.WithColor(DiscordConstants.InformationColorBlue);
        this._embed.WithDescription(setReply);
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("privacy", RunMode = RunMode.Async)]
    [Summary("Changes your visibility to other .fmbot users.\n\n" +
             "The default privacy setting is 'Server'.")]
    [Options("**Global**: You are visible in the global WhoKnows with your Last.fm username",
        "**Server**: You are not visible in global WhoKnows, but users in the same server will still see your name.")]
    [Examples("privacy global", "privacy server")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task PrivacyAsync(params string[] otherSettings)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        if (otherSettings == null || otherSettings.Length < 1 || otherSettings.First() == "info")
        {
            var replyString = $"Use {prfx}privacy to change your visibility to other .fmbot users.";

            this._embed.AddField("Options:",
                "**Global**: You are visible in the global WhoKnows with your Last.fm username\n" +
                "**Server**: You are not visible in global WhoKnows, but users in the same server will still see your name.\n\n" +
                "The default privacy setting is 'Server'.");

            this._embed.AddField("Examples:",
                $"`{prfx}privacy global` \n" +
                $"`{prfx}privacy server`");

            this._embed.WithTitle("Changing your .fmbot privacy mode");
            this._embed.WithUrl($"{Constants.DocsUrl}/commands/");
            this._embed.WithDescription(replyString);

            this._embed.WithFooter(
                $"Current privacy mode: {userSettings.PrivacyLevel}");

            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        var newPrivacyLevel = await this._userService.SetPrivacy(userSettings, otherSettings);

        var setReply = $"Your privacy mode has been set to **{newPrivacyLevel}**.\n\n";

        if (newPrivacyLevel == PrivacyLevel.Global)
        {
            setReply += " You will now be visible in the global WhoKnows with your Last.fm username.";
        }
        if (newPrivacyLevel == PrivacyLevel.Server)
        {
            setReply += " You will not be visible in the global WhoKnows with your Last.fm username, but users you share a server with will still see it.";
        }

        this._embed.WithColor(DiscordConstants.InformationColorBlue);
        this._embed.WithDescription(setReply);
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("login", RunMode = RunMode.Async)]
    [Summary("Logs you in using a link.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [Alias("set", "setusername", "fm set", "connect")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task LoginAsync([Remainder] string unusedValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var msg = this.Context.Message as SocketUserMessage;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddMinutes(1) >= DateTimeOffset.Now)
            {
                await ReplyAsync($"A login link has already been sent to your DMs.\n" +
                                 $"Didn't receive a link? Please check if you have DMs enabled for this server and try again.\n" +
                                 $"Setting location: Click on the server name (top left) > `Privacy Settings` > `Allow direct messages from server members`.");

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
        var token = await this._lastFmRepository.GetAuthToken();

        var reply = new StringBuilder();
        var link =
            $"http://www.last.fm/api/auth/?api_key={this._botSettings.LastFm.PublicKey}&token={token.Content.Token}";

        if (existingUserSettings == null)
        {
            reply.AppendLine($"**[Click here add your Last.fm account to .fmbot]({link})**");
            reply.AppendLine();
            reply.AppendLine("Link will expire after 5 minutes, please wait a moment after allowing access...");
            reply.AppendLine();
            reply.AppendLine("Don't have a Last.fm account yet? \n" +
                             "[Sign up here](https://last.fm/join) and see [how to track your music here](https://last.fm/about/trackmymusic).");
        }
        else
        {
            reply.AppendLine(
                $"You have already logged in before. If you want to change or reconnect your connected Last.fm account, [click here.]({link}) " +
                $"Note that this link will expire after 5 minutes.");
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
                var description =
                    $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({Constants.LastFMUserUrl}{newUserSettings.UserNameLastFM})!\n\n" +
                    $"`.fmmode` has been set to: `{newUserSettings.FmEmbedType}`\n" +
                    $"`.wkmode` has been set to: `{newUserSettings.Mode ?? WhoKnowsMode.Embed}`\n" +
                    $"`.fmprivacy` has been set to: `{newUserSettings.PrivacyLevel}`";

                var sourceGuildId = this.Context.Guild?.Id;
                var sourceChannelId = this.Context.Channel?.Id;

                if (sourceGuildId != null && sourceChannelId != null)
                {
                    description += "\n\n" +
                                   $"**[Click here to go back to <#{sourceChannelId}>](https://discord.com/channels/{sourceGuildId}/{sourceChannelId}/)**";
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
                                     $"Having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.")
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
        var userSettings = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (userSettings == null)
        {
            await ReplyAsync("Sorry, but we don't have any data from you in our database.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        if (string.IsNullOrEmpty(confirmation) || confirmation.ToLower() != "confirm")
        {
            var embed = UserBuilder.GetRemoveDataEmbed(userSettings, prfx);

            await this.Context.Channel.SendMessageAsync("", false, embed.Build());
        }
        else
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
            await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

            await this._userService.DeleteUser(userSettings.UserId);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted from .fmbot.");
        }

        this.Context.LogCommandUsed();
    }
}
