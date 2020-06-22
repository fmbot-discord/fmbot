using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands
{
    [Summary("FMBot Admins Only")]
    public class AdminCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly Logger.Logger _logger;
        private readonly TimerService _timer;

        private readonly EmbedBuilder _embed;

        private readonly UserService _userService;
        private readonly GuildService _guildService;

        public AdminCommands(TimerService timer, Logger.Logger logger, GuildService guildService)
        {
            this._timer = timer;
            this._logger = logger;
            this._guildService = guildService;
            this._userService = new UserService();
            this._adminService = new AdminService();
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
        }

        [Command("debug")]
        [Summary("Returns user data")]
        [Alias("dbcheck")]
        public async Task DebugAsync(IUser user = null)
        {
            var chosenUser = user ?? this.Context.Message.Author;
            var userSettings = await this._userService.GetFullUserAsync(chosenUser);

            if (userSettings?.UserNameLastFM == null)
            {
                await ReplyAsync("The user's Last.FM name has not been set.");
                return;
            }

            this._embed.WithTitle($"Debug for {chosenUser.ToString()}");

            var description = "";
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(userSettings))
            {
                var name = descriptor.Name;
                var value = descriptor.GetValue(userSettings);

                if (descriptor.PropertyType.Name == "ICollection`1")
                {
                    continue;
                }

                if (value != null)
                {
                    description += $"{name}: `{value}` \n";
                }
                else
                {
                    description += $"{name}: null \n";
                }
            }

            description += $"Friends: `{userSettings.Friends.Count}`\n";
            description += $"Befriended by: `{userSettings.FriendedByUsers.Count}`\n";
            description += $"Indexed artists: `{userSettings.Artists.Count}`";

            this._embed.WithDescription(description);
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        }


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
        }

        [Command("botrestart")]
        [Summary("Reboots the bot.")]
        [Alias("restart")]
        public async Task BotRestartAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync("Restarting bot...");
                await (this.Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                Environment.Exit(1);
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can restart the bot.");
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
                    return;
                }

                try
                {
                    this._timer.SetFeatured(url, desc, stopTimer);

                    await ReplyAsync("Set avatar to '" + url + "' with description '" + desc + "'. Timer stopped: " + stopTimer);

                }
                catch (Exception e)
                {
                    await ReplyAsync($"Something went wrong: {e.Message}");
                }
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
        //            await ReplyAsync(cfgjson.CommandPrefix + "fmavataroverride <image link> [message in quotation marks] [event 0 or 1]");
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
                }
                catch (Exception e)
                {
                    this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                        this.Context.Guild?.Name, this.Context.Guild?.Id);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
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
                }
                catch (Exception e)
                {
                    this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                        this.Context.Guild?.Name, this.Context.Guild?.Id);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
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
                }
                catch (Exception e)
                {
                    this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                        this.Context.Guild?.Name, this.Context.Guild?.Id);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
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
                        return;
                    }

                    if (user == this.Context.Message.Author)
                    {
                        await ReplyAsync("You cannot blacklist yourself!");
                        return;
                    }


                    var UserID = user.Id.ToString();

                    var blacklistresult = await this._adminService.AddUserToBlacklistAsync(user.Id);

                    if (blacklistresult)
                    {
                        await ReplyAsync("Added " + user.Username + " to the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already added " + user.Username +
                                         " to the blacklist or the blacklist does not exist for this user.");
                    }
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command.");
                }
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username, this.Context.Guild?.Name,
                    this.Context.Guild?.Id);

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
                        return;
                    }

                    var UserID = user.Id.ToString();

                    var blacklistresult = await this._adminService.RemoveUserFromBlacklistAsync(user.Id);

                    if (blacklistresult)
                    {
                        await ReplyAsync("Removed " + user.Username + " from the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already removed " + user.Username + " from the blacklist.");
                    }
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command.");
                }
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username, this.Context.Guild?.Name,
                    this.Context.Guild?.Id);

                await ReplyAsync("Unable to remove " + user.Username + " from the blacklist due to an internal error.");
            }
        }

        private bool IsTAnEnumerable<T>(T x)
        {
            return null != typeof(T).GetInterface("IEnumerable`1");
        }
    }
}
