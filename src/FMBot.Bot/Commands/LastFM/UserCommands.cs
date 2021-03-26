using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.API.Rest;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Serilog;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("User settings")]
    public class UserCommands : ModuleBase
    {
        private readonly CrownService _crownService;
        private readonly FriendsService _friendsService;
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmService _lastFmService;
        private readonly SettingService _settingService;
        private readonly TimerService _timer;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public UserCommands(
                FriendsService friendsService,
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                LastFmService lastFmService,
                SettingService settingService,
                TimerService timer,
                UserService userService,
                CrownService crownService)
        {
            this._friendsService = friendsService;
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmService = lastFmService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._timer = timer;
            this._userService = userService;
            this._crownService = crownService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("stats", RunMode = RunMode.Async)]
        [Summary("Displays user stats related to Last.fm and .fmbot")]
        [UsernameSetRequired]
        public async Task StatsAsync([Remainder] string userOptions = null)
        {
            var user = await this._userService.GetFullUserAsync(this.Context.User.Id);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            try
            {
                var userSettings = await this._settingService.GetUser(userOptions, user, this.Context);

                string userTitle;
                if (userSettings.DifferentUser)
                {
                    userTitle =
                        $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                    user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
                }
                else
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName(".fmbot and last.fm stats for " + userTitle);
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}");
                this._embed.WithAuthor(this._embedAuthor);

                this._embedFooter.WithText(
                    $"To see stats for other .fmbot users, use {prfx}stats '@user'");
                this._embed.WithFooter(this._embedFooter);

                var userInfo = await this._lastFmService.GetFullUserInfoAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm);

                var userImages = userInfo.Image;
                var userAvatar = userImages.FirstOrDefault(f => f.Size == "extralarge");

                if (userAvatar != null && !string.IsNullOrWhiteSpace(userAvatar.Text))
                {
                    this._embed.WithThumbnailUrl(userAvatar.Text);
                }

                var fmbotStats = new StringBuilder();

                fmbotStats.AppendLine($"Bot usertype: `{user.UserType}`");

                fmbotStats.AppendLine($".fm embed type: `{user.FmEmbedType}`");
                fmbotStats.AppendLine($"Last update: `{user.LastUpdated} (UTC)`");
                fmbotStats.AppendLine($"Last index: `{user.LastIndexed} (UTC)`");
                if (user.Friends?.Count > 0)
                {
                    fmbotStats.AppendLine($"Friends: `{user.Friends?.Count}`");
                }
                if (user.FriendedByUsers?.Count > 0)
                {
                    fmbotStats.AppendLine($"Befriended by: `{user.FriendedByUsers?.Count}`");
                }

                var lastFmStats = new StringBuilder();

                lastFmStats.AppendLine($"Username: [`{userSettings.UserNameLastFm}`]({Constants.LastFMUserUrl}{userSettings.UserNameLastFm})");
                lastFmStats.AppendLine($"Name: `{userInfo.Name}`");

                if (!userSettings.DifferentUser)
                {
                    var authorized = !string.IsNullOrEmpty(user.SessionKeyLastFm) ? "Yes" : "No";
                    lastFmStats.AppendLine($".fmbot authorized: `{authorized}`");
                }
                lastFmStats.AppendLine($"User type: `{userInfo.Type}`");
                lastFmStats.AppendLine($"Scrobbles: `{userInfo.Playcount}`");
                lastFmStats.AppendLine($"Country: `{userInfo.Country}`");

                this._embed.AddField("last.fm info", lastFmStats.ToString(), true);
                this._embed.AddField(".fmbot info", fmbotStats.ToString(), true);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show your stats due to an internal error.");
            }
        }

        [Command("featured", RunMode = RunMode.Async)]
        [Summary("Displays the featured avatar.")]
        [Alias("featuredavatar", "featureduser", "featuredalbum", "avatar")]
        public async Task FeaturedAsync()
        {
            try
            {
                var selfUser = this.Context.Client.CurrentUser;
                this._embed.WithThumbnailUrl(selfUser.GetAvatarUrl());
                this._embed.AddField("Featured:", this._timer.GetTrackString());

                if (this.Context.Guild != null)
                {
                    var guildUser =
                        await this._guildService.GetUserFromGuild(this.Context.Guild.Id, this._timer.GetUserId());

                    if (guildUser != null)
                    {
                        this._embed.WithFooter($"🥳 Congratulations! This user is in your server under the name {guildUser.UserName}.");
                    }
                }

                if (PublicProperties.IssuesAtLastFm)
                {
                    this._embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show the featured avatar on FMBot due to an internal error. \n" +
                    "The bot might not have changed its avatar since its last startup. Please wait until a new featured user is chosen.");
            }
        }

        [Command("rateyourmusic", RunMode = RunMode.Async)]
        [Summary("Enables or disables the rateyourmusic links.")]
        [Alias("rym")]
        public async Task RateYourMusicAsync()
        {
            try
            {
                var user = await this._userService.GetUserSettingsAsync(this.Context.User);

                var newRymSetting = await this._userService.ToggleRymAsync(user);

                if (newRymSetting == true)
                {
                    this._embed.WithDescription(
                        "**RateYourMusic** integration has now been enabled for your account.\n\n" +
                        "All album links should now link to RateYourMusic. Please note that since RYM does not provide an API, you might be routed through DuckDuckGo.\n" +
                        "This is not the best solution, but it should work for most albums.");
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
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Error while attempting to toggle rateyourmusic integration");
            }
        }

        [Command("botscrobbling", RunMode = RunMode.Async)]
        [Summary("Enables or disables the bot scrobbling.")]
        [Alias("botscrobble", "bottrack", "bottracking")]
        public async Task BotTrackingAsync([Remainder]string option = null)
        { 
            try
            {
                var user = await this._userService.GetUserSettingsAsync(this.Context.User);

                var newBotScrobblingDisabledSetting = await this._userService.ToggleBotScrobblingAsync(user, option);

                this._embed.WithDescription("Bot scrobbling allows you to automatically scrobble music from Discord music bots to your Last.fm account. " +
                                            "For this to work properly you need to make sure .fmbot can see the voice channel and use a supported music bot.\n\n" +
                                            "Only tracks that already exist on Last.fm will be scrobbled.\n\n" +
                                            "Currently supported bots:\n" +
                                            "- Groovy\n");

                if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && !string.IsNullOrWhiteSpace(user.SessionKeyLastFm))
                {
                    this._embed.AddField("Status", "✅ Enabled and ready.");
                    this._embed.WithFooter("Use '.fmbotscrobbling off' to disable.");
                }
                else if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && string.IsNullOrWhiteSpace(user.SessionKeyLastFm))
                {
                    this._embed.AddField("Status", "⚠️ Bot scrobbling is enabled, but you need to login through `.fmlogin` first.");
                }
                else
                {
                    this._embed.AddField("Status", "❌ Disabled. Do '.fmbotscrobbling on' to enable.");
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Error while attempting to change bot scrobbling setting");
            }
        }

        [Command("set", RunMode = RunMode.Async)]
        [Summary(
            "Sets your Last.fm name and FM mode. Please note that users in shared servers will be able to see and request your Last.fm username.")]
        [Alias("setname", "setmode", "fm set")]
        public async Task SetAsync([Summary("Your Last.fm name")] string lastFmUserName = null,
            params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var existingUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (lastFmUserName == null || lastFmUserName == "help")
            {
                var replyString = $"{prfx}set is the command you use to set your last.fm username in the bot, so it knows who you are on the last.fm website. \n" +
                                  "Don't have a last.fm account yet? Register here: https://www.last.fm/join \n \n" +
                                  "Sets your username, mode and playcount for the `.fm` command:\n \n" +
                                  $"`{prfx}set 'Last.fm Username' 'embedmini/embedfull/textmini/textfull' 'artist/album/track'` \n \n";

                if (existingUserSettings?.UserNameLastFM != null)
                {
                    var differentMode = existingUserSettings.FmEmbedType == FmEmbedType.EmbedMini ? "embedfull" : "embedmini";
                    replyString += "Example of picking a different mode: \n" +
                                   $"`{prfx}set {existingUserSettings.UserNameLastFM} {differentMode} album`";

                    replyString += "\n\n⚠️ Changing your Last.fm username with this command will clear all your crowns.\n" +
                                   $"Please use `{prfx}login` instead when you changed your Last.fm username and want to keep your crowns.";
                }
                else
                {
                    replyString += "Example of picking a mode and playcount: \n" +
                                   $"`{prfx}set lastfmusername embedfull artist`\n" +
                                   $"*Replace `lastfmusername` with your own last.fm username*";
                }

                this._embed.WithTitle("Changing your .fmbot settings");
                this._embed.WithUrl($"{Constants.DocsUrl}/commands/");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            lastFmUserName = lastFmUserName.Replace("'", "");
            if (!await this._lastFmService.LastFmUserExistsAsync(lastFmUserName))
            {
                var reply = $"Last.fm user `{lastFmUserName}` could not be found. Please check if the name you entered is correct.";
                await ReplyAsync(reply.FilterOutMentions());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }
            if (lastFmUserName == "lastfmusername")
            {
                await ReplyAsync("Please enter your own Last.fm username and not `lastfmusername`.\n");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var usernameChanged = !(existingUserSettings?.UserNameLastFM != null &&
                                     string.Equals(existingUserSettings.UserNameLastFM, lastFmUserName, StringComparison.CurrentCultureIgnoreCase));

            var userSettingsToAdd = new User
            {
                UserNameLastFM = lastFmUserName,
            };

            userSettingsToAdd = this._userService.SetSettings(userSettingsToAdd, otherSettings);

            await this._userService.SetLastFm(this.Context.User, userSettingsToAdd);

            var setReply = $"Your Last.fm name has been set to '{lastFmUserName}' and your .fm mode to '{userSettingsToAdd.FmEmbedType}'";
            if (userSettingsToAdd.FmCountType != null)
            {
                setReply += $" with the '{userSettingsToAdd.FmCountType.ToString().ToLower()}' playcount.";
            }

            if (otherSettings.Length < 1)
            {
                setReply += $". \nWant more info about the different modes? Use `{prfx}set help`";
            }

            await ReplyAsync(setReply.FilterOutMentions());

            this.Context.LogCommandUsed();

            var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (usernameChanged)
            {
                await this._indexService.IndexUser(newUserSettings);
                await this._crownService.RemoveAllCrownsFromUser(newUserSettings.UserId);
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
                if (guild != null)
                {
                    await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(this.Context.User.Id), newUserSettings);
                }

                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync(
                        "Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.");
                }
            }
        }

        [Command("mode", RunMode = RunMode.Async)]
        [Summary("Change your settings for how your .fm looks")]
        [Alias("m", "md", "fmmode")]
        [UsernameSetRequired]
        public async Task ModeAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (otherSettings == null || otherSettings.Length < 1 || otherSettings.First() == "help")
            {
                var replyString = $"Use {prfx}mode to change how your .fm command looks.";

                this._embed.AddField("Options",
                    "**Modes**: `embedtiny/embedmini/embedfull/textmini/textfull`\n" +
                    "**Playcounts**: `artist/album/track`\n" +
                    "*Note: Playcounts are only visible in non-text modes.*\n\n" +
                    $"Server mode can be set using `{prfx}servermode`. The server mode overrules any mode set by users.");

                this._embed.AddField("Examples",
                    $"`{prfx}mode embedmini` \n" +
                    $"`{prfx}mode embedfull track`\n" +
                    $"`{prfx}mode textfull`\n" +
                    $"`{prfx}mode embedtiny album`");

                this._embed.WithTitle("Changing your .fm command");
                this._embed.WithUrl($"{Constants.DocsUrl}/commands/");

                var countType = !userSettings.FmCountType.HasValue ? "No extra playcount" : userSettings.FmCountType.ToString();
                this._embed.WithFooter(
                    $"Current mode and playcount: {userSettings.FmEmbedType} - {countType}");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var newUserSettings = this._userService.SetSettings(userSettings, otherSettings);

            await this._userService.SetLastFm(this.Context.User, newUserSettings);

            var setReply = $"Your `.fm` mode has been set to '{newUserSettings.FmEmbedType}'";
            if (newUserSettings.FmCountType != null)
            {
                setReply += $" with the '{newUserSettings.FmCountType.ToString().ToLower()}' playcount.";
            }
            else
            {
                setReply += $" with no extra playcount.";
            }

            setReply +=
                "\n\nNote that servers can force an .fm mode. The server setting will always overrule your own .fm mode.";

            await ReplyAsync(setReply.FilterOutMentions());

            this.Context.LogCommandUsed();
        }

        [Command("privacy", RunMode = RunMode.Async)]
        [Summary("Change your privacy mode for .fmbot")]
        [UsernameSetRequired]
        public async Task PrivacyAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (otherSettings == null || otherSettings.Length < 1 || otherSettings.First() == "help")
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

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var newPrivacyLevel = await this._userService.SetPrivacy(userSettings, otherSettings);

            var setReply = $"Your privacy mode has been set to '{newPrivacyLevel}'.";

            if (newPrivacyLevel == PrivacyLevel.Global)
            {
                setReply += " You will now be visible in the global WhoKnows with your Last.fm username.";
            }
            if (newPrivacyLevel == PrivacyLevel.Server)
            {
                setReply += " You will not be visible in the global WhoKnows with your Last.fm username, but users you share a server with will still see it.";
            }

            await ReplyAsync(setReply.FilterOutMentions());

            this.Context.LogCommandUsed();
        }

        [Command("login", RunMode = RunMode.Async)]
        [Summary("Logs you in using a link")]
        public async Task LoginAsync([Remainder] string unusedValues = null)
        {
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
            var token = await this._lastFmService.GetAuthToken();

            // TODO: When our Discord library supports follow up messages for interactions, add slash command support.
            var replyString =
                $"[Click here add your Last.fm account to .fmbot](http://www.last.fm/api/auth/?api_key={ConfigData.Data.LastFm.Key}&token={token.Content.Token})";

            this._embed.WithTitle("Logging into .fmbot...");
            this._embed.WithDescription(replyString);

            this._embedFooter.WithText("Link will expire after 2 minutes, please wait a moment after allowing access...");
            this._embed.WithFooter(this._embedFooter);

            var authorizeMessage = await this.Context.User.SendMessageAsync("", false, this._embed.Build());

            if (!this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Check your DMs for a link to connect your Last.fm account to .fmbot!");
            }

            var success = await GetAndStoreAuthSession(this.Context.User, token.Content.Token);

            if (success)
            {
                var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
                await authorizeMessage.ModifyAsync(m =>
                {
                    var description =
                        $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({Constants.LastFMUserUrl}{newUserSettings.UserNameLastFM})!\n\n" +
                        $"`.fmmode` has been set to: `{newUserSettings.FmEmbedType}`\n" +
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
                    var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
                    if (guild != null)
                    {
                        await this._indexService.GetOrAddUserToGuild(guild,
                            await this.Context.Guild.GetUserAsync(this.Context.User.Id), newUserSettings);
                    }
                }
            }
            else
            {
                await authorizeMessage.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription($"❌ Login failed.. link expired or something went wrong.\n\n" +
                                         $"Having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.")
                        .WithColor(DiscordConstants.WarningColorOrange)
                        .Build();
                });

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
            }
        }

        private async Task<bool> GetAndStoreAuthSession(IUser contextUser, string token)
        {
            var loginDelay = 7000;
            for (var i = 0; i < 9; i++)
            {
                await Task.Delay(loginDelay);

                var authSession = await this._lastFmService.GetAuthSession(token);

                if (authSession.Success)
                {
                    var userSettings = new User
                    {
                        UserNameLastFM = authSession.Content.Session.Name,
                        SessionKeyLastFm = authSession.Content.Session.Key
                    };

                    Log.Information("LastfmAuth: User {userName} logged in with auth session (discordUserId: {discordUserId})", authSession.Content.Session.Name, contextUser.Id);
                    await this._userService.SetLastFm(contextUser, userSettings, true);
                    return true;
                }

                if (!authSession.Success && i == 8)
                {
                    Log.Information("LastfmAuth: Login timed out or auth not successful (discordUserId: {discordUserId})", contextUser.Id);
                    return false;
                }
                if (!authSession.Success)
                {
                    loginDelay += 2000;
                    Log.Information("LastfmAuth: Login attempt {attempt} for {user} | {discordUserId} not succeeded yet ({errorCode}), delaying", i, contextUser.Username, contextUser.Id, authSession.Message);
                }
            }

            return false;
        }

        [Command("remove", RunMode = RunMode.Async)]
        [Summary("Deletes your FMBot data.")]
        [Alias("delete", "removedata", "deletedata")]
        public async Task RemoveAsync([Remainder] string confirmation = null)
        {
            var userSettings = await this._userService.GetFullUserAsync(this.Context.User.Id);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (string.IsNullOrEmpty(confirmation) || confirmation.ToLower() != "confirm")
            {
                var sb = new StringBuilder();
                sb.AppendLine("Are you sure you want to delete all your data from .fmbot?");
                sb.AppendLine("This will remove the following data:");

                sb.AppendLine("- Your last.fm username");
                if (userSettings.Friends?.Count > 0)
                {
                    var friendString = userSettings.Friends?.Count == 1 ? "friend" : "friends";
                    sb.AppendLine($"- `{userSettings.Friends?.Count}` {friendString}");
                }

                if (userSettings.FriendedByUsers?.Count > 0)
                {
                    var friendString = userSettings.FriendedByUsers?.Count == 1 ? "friendlist" : "friendlists";
                    sb.AppendLine($"- You from `{userSettings.FriendedByUsers?.Count}` other {friendString}");
                }

                sb.AppendLine("- Indexed artists, albums and tracks");
                sb.AppendLine("- All crowns you've gained or lost");

                if (userSettings.UserType != UserType.User)
                {
                    sb.AppendLine($"- `{userSettings.UserType}` account status");
                    sb.AppendLine("*Account status has to be manually changed back by an .fmbot admin*");
                }

                sb.AppendLine();
                sb.AppendLine($"Type `{prfx}remove confirm` to confirm deletion.");

                this._embed.WithDescription(sb.ToString());

                this._embed.WithFooter("Note: This will not delete any data from Last.fm, just from .fmbot.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
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

        [Command("suggest", RunMode = RunMode.Async)]
        [Summary("Suggest features you want to see in the bot, or report inappropriate images.")]
        [Alias("report", "suggestion", "suggestions")]
        public async Task Suggest(string suggestion = null)
        {
            try
            {
                /*
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    await ReplyAsync(cfgjson.Prefix + "fmsuggest 'text in quotes'");
                    return;
                }
                else
                {
                */

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName(this.Context.User.ToString());
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithTitle(this.Context.User.Username + "'s suggestion:");
                this._embed.WithDescription(suggestion);
                this._embed.WithTimestamp(DateTimeOffset.UtcNow);

                var BroadcastServerID = ConfigData.Data.Bot.BaseServerId;
                var BroadcastChannelID = ConfigData.Data.Bot.SuggestionChannelId;

                var guild = await this.Context.Client.GetGuildAsync(BroadcastServerID);
                var channel = await guild.GetChannelAsync(BroadcastChannelID);



                await ReplyAsync("Your suggestion has been sent to the .fmbot server!");
                this.Context.LogCommandUsed();

                //}
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
            }
        }
    }
}
