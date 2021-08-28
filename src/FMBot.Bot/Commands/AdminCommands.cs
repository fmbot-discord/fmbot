using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands
{
    [Name("Admin settings")]
    [Summary(".fmbot Admins Only")]
    [ExcludeFromHelp]
    public class AdminCommands : BaseCommandModule
    {
        private readonly AdminService _adminService;
        private readonly CensorService _censorService;
        private readonly GuildService _guildService;
        private readonly TimerService _timer;
        private readonly LastFmRepository _lastFmRepository;
        private readonly SupporterService _supporterService;
        private readonly UserService _userService;
        private readonly SettingService _settingService;

        public AdminCommands(
                AdminService adminService,
                CensorService censorService,
                GuildService guildService,
                TimerService timer,
                LastFmRepository lastFmRepository,
                SupporterService supporterService,
                UserService userService,
                IOptions<BotSettings> botSettings,
                SettingService settingService) : base(botSettings)
        {
            this._adminService = adminService;
            this._censorService = censorService;
            this._guildService = guildService;
            this._timer = timer;
            this._lastFmRepository = lastFmRepository;
            this._supporterService = supporterService;
            this._userService = userService;
            this._settingService = settingService;
        }

        //[Command("debug")]
        //[Summary("Returns user data")]
        //[Alias("dbcheck")]
        //public async Task DebugAsync(IUser user = null)
        //{
        //    var chosenUser = user ?? this.Context.Message.Author;
        //    var userSettings = await this._userService.GetFullUserAsync(chosenUser);

        //    if (userSettings?.UserNameLastFM == null)
        //    {
        //        await ReplyAsync("The user's Last.fm name has not been set.");
        //        this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
        //        return;
        //    }

        //    this._embed.WithTitle($"Debug for {chosenUser.ToString()}");

        //    var description = "";
        //    foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(userSettings))
        //    {
        //        var name = descriptor.Name;
        //        var value = descriptor.GetValue(userSettings);

        //        if (descriptor.PropertyType.Name == "ICollection`1")
        //        {
        //            continue;
        //        }

        //        if (value != null)
        //        {
        //            description += $"{name}: `{value}` \n";
        //        }
        //        else
        //        {
        //            description += $"{name}: null \n";
        //        }
        //    }

        //    description += $"Friends: `{userSettings.Friends.Count}`\n";
        //    description += $"Befriended by: `{userSettings.FriendedByUsers.Count}`\n";
        //    //description += $"Indexed artists: `{userSettings.Artists.Count}`";
        //    //description += $"Indexed albums: `{userSettings.Albums.Count}`";
        //    //description += $"Indexed tracks: `{userSettings.Tracks.Count}`";

        //    this._embed.WithDescription(description);
        //    await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        //    this.Context.LogCommandUsed();
        //}


        [Command("serverdebug")]
        [Summary("Returns server data")]
        [Alias("guilddebug", "debugserver", "debugguild")]
        public async Task DebugGuildAsync(ulong? guildId = null)
        {
            var chosenGuild = guildId ?? this.Context.Guild.Id;
            var guild = await this._guildService.GetFullGuildAsync(chosenGuild);

            if (guild == null)
            {
                await ReplyAsync("Guild does not exist in database");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            this._embed.WithTitle($"Debug for guild with id {chosenGuild}");

            var description = "";
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(guild))
            {
                var name = descriptor.Name;
                var value = descriptor.GetValue(guild);

                if (value == null)
                {
                    description += $"{name}: null \n";
                    continue;
                }

                if (descriptor.PropertyType.Name == "String[]")
                {
                    var a = (Array)descriptor.GetValue(guild);
                    var arrayValue = "";
                    for (var i = 0; i < a.Length; i++)
                    {
                        arrayValue += $"{a.GetValue(i)} - ";
                    }

                    if (a.Length > 0)
                    {
                        description += $"{name}: `{arrayValue}` \n";
                    }
                    else
                    {
                        description += $"{name}: null \n";
                    }
                }
                else
                {
                    description += $"{name}: `{value}` \n";
                }
            }

            this._embed.WithDescription(description);
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }

        [Command("issues")]
        [Summary("Toggles issue mode")]
        public async Task IssuesAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!PublicProperties.IssuesAtLastFm)
                {
                    PublicProperties.IssuesAtLastFm = true;
                    await ReplyAsync("Enabled issue mode");

                }
                else
                {
                    PublicProperties.IssuesAtLastFm = false;
                    await ReplyAsync("Disabled issue mode");
                }

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can change issue mode.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("purgecache")]
        [Summary("Purges discord caches")]
        public async Task PurgeCacheAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var client = this.Context.Client as DiscordShardedClient;
                if (client == null)
                {
                    await ReplyAsync("Client is null");
                    return;
                }

                var currentProcess = Process.GetCurrentProcess();
                var currentMemoryUsage = currentProcess.WorkingSet64;
                var reply = new StringBuilder();

                reply.AppendLine("Purged user, channel, DM channel cache and ran garbage collector.");
                reply.AppendLine($"Memory before purge: `{currentMemoryUsage.ToFormattedByteString()}`");

                foreach (var socketClient in client.Shards)
                {
                    socketClient.PurgeUserCache();
                }

                GC.Collect();

                await Task.Delay(2000);

                currentProcess = Process.GetCurrentProcess();
                currentMemoryUsage = currentProcess.WorkingSet64;

                reply.AppendLine($"Memory after purge: `{currentMemoryUsage.ToFormattedByteString()}`");

                await ReplyAsync(reply.ToString());

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can change purge cache.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("addcensoredalbum")]
        [Summary("Adds censored album")]
        public async Task AddCensoredAlbumAsync(string album, string artist)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(album) || string.IsNullOrEmpty(artist))
                {
                    await ReplyAsync("Enter a correct album to be censored\n" +
                                     "Example: `.fmaddcensoredalbum \"No Love Deep Web\" \"Death Grips\"");
                    return;
                }

                await this._censorService.AddCensoredAlbum(album, artist);

                await ReplyAsync($"Added `{album}` by `{artist}` to the list of censored albums.");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins add censored albums.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("addnsfwalbum")]
        [Summary("Adds nsfw album")]
        public async Task AddNsfwAlbumAsync(string album, string artist)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(album) || string.IsNullOrEmpty(artist))
                {
                    await ReplyAsync("Enter a correct album to be censored\n" +
                                     "Example: `.fmaddnsfwalbum \"No Love Deep Web\" \"Death Grips\"");
                    return;
                }

                await this._censorService.AddNsfwAlbum(album, artist);

                await ReplyAsync($"Added `{album}` by `{artist}` to the list of nsfw albums.");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins add nsfw albums.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("addcensoredartist")]
        [Summary("Adds censored artist")]
        public async Task AddCensoredArtistAsync(string artist)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(artist))
                {
                    await ReplyAsync("Enter a correct artist to be censored\n" +
                                     "Example: `.fmaddcensoredartist \"Last Days of Humanity\"");
                    return;
                }

                await this._censorService.AddCensoredArtist(artist);

                await ReplyAsync($"Added `{artist}` to the list of censored artists.");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins add censored artists.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("checkbotted")]
        [Summary("Checks some stats for a user and if they're banned from global whoknows")]
        public async Task CheckBottedUserAsync(string user = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(user))
                {
                    await ReplyAsync("Enter an username to check\n" +
                                     "Example: `.fmcheckbotted Kefkef123`");
                    return;
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
                var targetedUser = await this._settingService.GetUser(user, contextUser, this.Context);

                if (targetedUser.DifferentUser)
                {
                    user = targetedUser.UserNameLastFm;
                }

                var bottedUser = await this._adminService.GetBottedUserAsync(user);

                var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(user);

                this._embed.WithTitle($"Botted check for Last.fm '{user}'");

                if (userInfo == null)
                {
                    this._embed.WithDescription($"Not found on Last.fm - [User]({Constants.LastFMUserUrl}{user})");
                }
                else
                {
                    this._embed.WithDescription($"[Profile]({Constants.LastFMUserUrl}{user}) - " +
                                                $"[Library]({Constants.LastFMUserUrl}{user}/library) - " +
                                                $"[Last.week]({Constants.LastFMUserUrl}{user}/listening-report) - " +
                                                $"[Last.year]({Constants.LastFMUserUrl}{user}/listening-report/year)");

                    var dateAgo = DateTime.UtcNow.AddDays(-365);
                    var timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

                    var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(user, timeFrom);

                    var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
                    var totalDays = (DateTime.UtcNow - age).TotalDays;

                    var avgPerDay = count / totalDays;
                    this._embed.AddField("Avg scrobbles / day in last year", Math.Round(avgPerDay.GetValueOrDefault(0), 1));
                }

                this._embed.AddField("Banned from GlobalWhoKnows", bottedUser == null ? "No" : bottedUser.BanActive ? "Yes" : "No, but has been banned before");
                if (bottedUser != null)
                {
                    this._embed.AddField("Reason / additional notes", bottedUser.Notes ?? "*No reason/notes*");
                }

                this._embed.WithFooter("Command not intended for use in public channels");

                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can check botted users");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("addbotteduser")]
        public async Task AddBottedUserAsync(string user = null, string reason = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(reason))
                {
                    await ReplyAsync("Enter an username and reason to remove someone from gwk banlist\n" +
                                     "Example: `.fmaddbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var bottedUser = await this._adminService.GetBottedUserAsync(user);
                if (bottedUser == null)
                {
                    if (!await this._adminService.AddBottedUserAsync(user, reason))
                    {
                        await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                        this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    }
                    else
                    {
                        await ReplyAsync($"User {user} has been banned from GlobalWhoKnows with reason '{reason.FilterOutMentions()}'");
                        this.Context.LogCommandUsed();
                    }
                }
                else
                {
                    if (!await this._adminService.EnableBottedUserBanAsync(user, reason))
                    {
                        await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                        this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    }
                    else
                    {
                        await ReplyAsync($"User {user} has been banned from GlobalWhoKnows with reason '{reason.FilterOutMentions()}'");
                        this.Context.LogCommandUsed();
                    }
                }

                
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can remove botted users");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("removebotteduser")]
        public async Task RemoveBottedUserAsync(string user = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (string.IsNullOrEmpty(user))
                {
                    await ReplyAsync("Enter an username to remove from the gwk banlist. This will flag their ban as `false`.\n" +
                                     "Example: `.fmremovebotteduser \"Kefkef123\"`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var bottedUser = await this._adminService.GetBottedUserAsync(user);
                if (bottedUser == null)
                {
                    await ReplyAsync("The specified user has never been banned from GlobalWhoKnows");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                if (!bottedUser.BanActive)
                {
                    await ReplyAsync("User is in banned user list, but their ban was already inactive");
                    return;
                }

                if (!await this._adminService.DisableBottedUserBanAsync(user))
                {
                    await ReplyAsync("The specified user has not been banned from GlobalWhoKnows");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
                else
                {
                    await ReplyAsync($"User {user} has been unbanned from GlobalWhoKnows");
                    this.Context.LogCommandUsed();
                    return;
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can remove botted users");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("addsupporter")]
        public async Task AddSupporterAsync(string user = null, string name = null, string internalNotes = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var formatError = "Make sure to follow the correct format when adding a supporter\n" +
                                  "Examples: \n" +
                                  "`.fmaddsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"`\n" +
                                  "`.fmaddsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"`";

                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(internalNotes) || string.IsNullOrEmpty(name) || user == "help")
                {
                    await ReplyAsync(formatError);
                    this.Context.LogCommandUsed(CommandResponse.Help);
                    return;
                }

                if (!ulong.TryParse(user, out var userId))
                {
                    await ReplyAsync(formatError);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var userSettings = await this._userService.GetUserAsync(userId);

                if (userSettings == null)
                {
                    await ReplyAsync("`User not found`\n\n" + formatError);
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }
                if (userSettings.UserType != UserType.User)
                {
                    await ReplyAsync("`Can only change usertype of normal users`\n\n" + formatError);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                await this._supporterService.AddSupporter(userSettings.DiscordUserId, name, internalNotes);

                this._embed.WithDescription("Supporter added.\n" +
                                            $"User id: {user} | <@{user}>\n" +
                                            $"Name: **{name}**\n" +
                                            $"Internal notes: `{internalNotes}`");

                this._embed.WithFooter("Command not intended for use in public channels");

                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can add supporters");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("fmfeaturedoverride"), Summary("Changes the avatar to be an album.")]
        public async Task AlbumOverrideAsync(string url, bool stopTimer, string desc = "Custom featured event")
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                if (url == "help")
                {
                    await ReplyAsync("fmfeaturedoverride <album name> <stoptimer 0 or 1> [message in quotation marks]");
                    this.Context.LogCommandUsed(CommandResponse.Help);
                    return;
                }

                try
                {
                    this._timer.SetFeatured(url, desc, stopTimer);

                    await ReplyAsync("Set avatar to '" + url + "' with description '" + desc + "'. Timer stopped: " + stopTimer);
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync($"Something went wrong: {e.Message}");
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot owners can set featured.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        //[Command("fmavataroverride"), Summary("Changes the avatar to be a image from a link.")]
        //[Alias("fmsetavatar")]
        //public async Task fmavataroverrideAsync(string link, string desc = "Custom FMBot Avatar", int ievent = 0)
        //{
        //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
        //    {
        //        JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

        //        if (link == "help")
        //        {
        //            await ReplyAsync(cfgjson.Prefix + "fmavataroverride <image link> [message in quotation marks] [event 0 or 1]");
        //            return;
        //        }

        //        try
        //        {
        //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;

        //            if (ievent == 1)
        //            {
        //                _timer.UseCustomAvatarFromLink(client, link, desc, true);
        //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
        //            }
        //            else
        //            {
        //                _timer.UseCustomAvatarFromLink(client, link, desc, false);
        //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is not an event.");
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
        //            ExceptionReporter.ReportException(client, e);
        //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
        //        }
        //    }
        //}

        //[Command("fmresetavatar"), Summary("Changes the avatar to be the default.")]
        //public async Task fmresetavatar()
        //{
        //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
        //    {
        //        try
        //        {
        //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
        //            _timer.UseDefaultAvatar(client);
        //            await ReplyAsync("Set avatar to 'FMBot Default'");
        //        }
        //        catch (Exception e)
        //        {
        //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
        //            ExceptionReporter.ReportException(client, e);
        //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
        //        }
        //    }
        //}

        [Command("resetfeatured")]
        [Summary("Restarts the featured timer.")]
        [Alias("restarttimer", "timerstart", "timerrestart")]
        public async Task RestartTimerAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                try
                {
                    this._timer.Restart();
                    await ReplyAsync("Featured timer restarted (note: max 3 times / hour)");
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can restart timer.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("stoptimer")]
        [Summary("Stops the internal bot avatar timer.")]
        [Alias("timerstop")]
        public async Task StopTimerAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                try
                {
                    this._timer.Stop();
                    await ReplyAsync("Timer stopped");
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("timerstatus")]
        [Summary("Checks the status of the timer.")]
        public async Task TimerStatusAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                try
                {
                    if (this._timer.IsTimerActive())
                    {
                        await ReplyAsync("Timer is active");
                    }
                    else
                    {
                        await ReplyAsync("Timer is inactive");
                    }
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can check timer.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("globalblacklistadd")]
        [Summary("Adds a user to the global FMBot blacklist.")]
        public async Task BlacklistAddAsync(SocketGuildUser user = null)
        {
            try
            {
                if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
                {
                    if (user == null)
                    {
                        await ReplyAsync("Please specify what user you want to add to the blacklist.");
                        this.Context.LogCommandUsed(CommandResponse.WrongInput);
                        return;
                    }

                    if (user == this.Context.Message.Author)
                    {
                        await ReplyAsync("You cannot blacklist yourself!");
                        this.Context.LogCommandUsed(CommandResponse.WrongInput);
                        return;
                    }

                    var blacklistResult = await this._adminService.AddUserToBlocklistAsync(user.Id);

                    if (blacklistResult)
                    {
                        await ReplyAsync("Added " + user.Username + " to the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already added " + user.Username +
                                         " to the blacklist or the blacklist does not exist for this user.");
                    }

                    this.Context.LogCommandUsed();
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                }
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to add " + user.Username + " to the blacklist due to an internal error.");
            }
        }

        [Command("globalblacklistremove")]
        [Summary("Removes a user from the global FMBot blacklist.")]
        public async Task BlackListRemoveAsync(SocketGuildUser user = null)
        {
            try
            {
                if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
                {
                    if (user == null)
                    {
                        await ReplyAsync("Please specify what user you want to remove from the blacklist.");
                        this.Context.LogCommandUsed(CommandResponse.WrongInput);
                        return;
                    }

                    var blacklistResult = await this._adminService.RemoveUserFromBlocklistAsync(user.Id);

                    if (blacklistResult)
                    {
                        await ReplyAsync("Removed " + user.Username + " from the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already removed " + user.Username + " from the blacklist.");
                    }
                    this.Context.LogCommandUsed();
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                }
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to remove " + user.Username + " from the blacklist due to an internal error.");
            }
        }
    }
}
