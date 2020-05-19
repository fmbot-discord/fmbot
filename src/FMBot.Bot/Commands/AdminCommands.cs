using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Commands
{
    [Summary("FMBot Admins Only")]
    public class AdminCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly Logger.Logger _logger;
        private readonly TimerService _timer;

        private readonly UserService _userService;

        public AdminCommands(TimerService timer, Logger.Logger logger)
        {
            this._timer = timer;
            this._logger = logger;
            this._userService = new UserService();
            this._adminService = new AdminService();
        }

        [Command("dbcheck")]
        [Summary("Checks if an entry is in the database.")]
        public async Task DbCheckAsync(IUser user = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var chosenUser = user ?? this.Context.Message.Author;
                var userSettings = await this._userService.GetUserSettingsAsync(chosenUser);

                if (userSettings?.UserNameLastFM == null)
                {
                    await ReplyAsync("The user's Last.FM name has not been set.");
                    return;
                }

                await ReplyAsync("The user's Last.FM name is '" + userSettings.UserNameLastFM +
                                 "'. Their mode is set to '" + userSettings.FmEmbedType + "'.");
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can do a dbcheck.");
            }
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


        //[Command("fmalbumoverride"), Summary("Changes the avatar to be an album.")]
        //[Alias("fmsetalbum")]
        //public async Task fmalbumoverrideAsync(string albumname, string desc = "Custom FMBot Album Avatar", int ievent = 0)
        //{
        //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
        //    {
        //        JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

        //        if (albumname == "help")
        //        {
        //            await ReplyAsync(cfgjson.CommandPrefix + "fmalbumoverride <album name> [message in quotation marks] [event 0 or 1]");
        //            return;
        //        }

        //        try
        //        {
        //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;

        //            if (ievent == 1)
        //            {
        //                _timer.UseCustomAvatar(client, albumname, desc, false, true);
        //                await ReplyAsync("Set avatar to '" + albumname + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
        //            }
        //            else
        //            {
        //                _timer.UseCustomAvatar(client, albumname, desc, false, false);
        //                await ReplyAsync("Set avatar to '" + albumname + "' with description '" + desc + "'. This is not an event.");
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
    }
}
