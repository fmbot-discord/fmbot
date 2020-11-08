using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands
{
    [Summary("FMBot Admins Only")]
    public class AdminCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly CensorService _censorService;
        private readonly GuildService _guildService;
        private readonly TimerService _timer;

        private readonly EmbedBuilder _embed;

        public AdminCommands(
                AdminService adminService,
                CensorService censorService,
                GuildService guildService,
                TimerService timer
            )
        {
            this._adminService = adminService;
            this._censorService = censorService;
            this._guildService = guildService;
            this._timer = timer;

            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
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
            var guild = await this._guildService.GetGuildAsync(chosenGuild);

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

        [Command("botrestart")]
        [Summary("Reboots the bot.")]
        [Alias("restart")]
        public async Task BotRestartAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync("Restarting bot...");
                this.Context.LogCommandUsed();
                await (this.Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                Environment.Exit(1);
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can restart the bot.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("issues")]
        [Summary("Toggles issue mode")]
        public async Task IssuesAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!PublicProperties.IssuesAtLastFM)
                {
                    PublicProperties.IssuesAtLastFM = true;
                    await ReplyAsync("Enabled issue mode");

                }
                else
                {
                    PublicProperties.IssuesAtLastFM = false;
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


        [Command("fmfeaturedoverride"), Summary("Changes the avatar to be an album.")]
        public async Task fmalbumoverrideAsync(string url, bool stopTimer, string desc = "Custom featured event")
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

        [Command("restarttimer")]
        [Summary("Restarts the internal bot avatar timer.")]
        [Alias("starttimer", "timerstart", "timerrestart")]
        public async Task RestartTimerAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                try
                {
                    this._timer.Restart();
                    await ReplyAsync("Timer restarted");
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
                await ReplyAsync("Error: Insufficient rights. Only FMBot owners can restart timer.");
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

                    var blacklistResult = await this._adminService.AddUserToBlacklistAsync(user.Id);

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

                    var blacklistResult = await this._adminService.RemoveUserFromBlacklistAsync(user.Id);

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
