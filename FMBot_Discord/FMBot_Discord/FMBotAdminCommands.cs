using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Data.Entities;
using FMBot.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot
{
    public class FMBotAdminCommands : ModuleBase
    {
        #region Constructor

        private readonly CommandService _service;
        private readonly TimerService _timer;

        private readonly UserService userService = new UserService();

        private readonly AdminService adminService = new AdminService();

        public FMBotAdminCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }

        #endregion

        #region FMBot Staff Only Commands

        [Command("fmdbcheck"), Summary("Checks if an entry is in the database. - FMBot Admin Only")]
        public async Task dbcheckAsync(IUser user = null)
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                IUser chosenUser = user ?? Context.Message.Author;
                Data.Entities.User userSettings = await userService.GetUserSettingsAsync(chosenUser);

                if (userSettings == null || userSettings.UserNameLastFM == null)
                {
                    await ReplyAsync("The user's Last.FM name has not been set.");
                    return;
                }

                await ReplyAsync("The user's Last.FM name is '" + userSettings.UserNameLastFM + "'. Their mode is set to '" + userSettings.ChartType + "'.");
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can do a dbcheck.");
            }
        }

        [Command("fmbotrestart"), Summary("Reboots the bot. - FMBot Admins only")]
        [Alias("fmrestart")]
        public async Task fmbotrestartAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                await ReplyAsync("Restarting bot...");
                await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                Environment.Exit(1);
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot admins can restart the bot.");
            }
        }

        [Command("fmsetusertype"), Summary("Gives permissions to other users. - FMBot Owners only")]
        [Alias("fmsetperms")]
        public async Task fmsetusertypeAsync(string userId = null, string userType = null)
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Owner))
            {
                if (userId == null || userType == null)
                {
                    await ReplyAsync("Please format your command like this: `.fmsetusertype 'discord id' 'User/Admin/Owner'`");
                    return;
                }

                if (!Enum.TryParse(userType, out UserType userTypeEnum))
                {
                    await ReplyAsync("Invalid usertype. Please use 'User', 'Admin', or 'Owner'.");
                    return;
                }

                if (await adminService.SetUserTypeAsync(userId, userTypeEnum))
                {
                    await ReplyAsync("You got it. User perms changed.");
                }
                else
                {
                    await ReplyAsync("Setting user failed. Are you sure the user exists?");
                }

            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot owners can change your usertype.");
            }
        }

        [Command("fmalbumoverride"), Summary("Changes the avatar to be an album. - FMBot Admins only")]
        [Alias("fmsetalbum")]
        public async Task fmalbumoverrideAsync(string albumname, string desc = "Custom FMBot Album Avatar", int ievent = 0)
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                if (albumname == "help")
                {
                    await ReplyAsync(cfgjson.CommandPrefix + "fmalbumoverride <album name> [message in quotation marks] [event 0 or 1]");
                    return;
                }

                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;

                    if (ievent == 1)
                    {
                        _timer.UseCustomAvatar(client, albumname, desc, false, true);
                        await ReplyAsync("Set avatar to '" + albumname + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
                    }
                    else
                    {
                        _timer.UseCustomAvatar(client, albumname, desc, false, false);
                        await ReplyAsync("Set avatar to '" + albumname + "' with description '" + desc + "'. This is not an event.");
                    }
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmartistoverride"), Summary("Changes the avatar to be an artist. - FMBot Admins only")]
        [Alias("fmsetartist")]
        public async Task fmartistoverrideAsync(string artistname, string desc = "Custom FMBot Artist Avatar", int ievent = 0)
        {
            IUser DiscordUser = Context.Message.Author;
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                if (artistname == "help")
                {
                    await ReplyAsync(cfgjson.CommandPrefix + "fmartistoverride <artist name> [message in quotation marks] [event 0 or 1]");
                    return;
                }

                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;

                    if (ievent == 1)
                    {
                        _timer.UseCustomAvatar(client, artistname, desc, true, true);
                        await ReplyAsync("Set avatar to '" + artistname + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
                    }
                    else
                    {
                        _timer.UseCustomAvatar(client, artistname, desc, true, false);
                        await ReplyAsync("Set avatar to '" + artistname + "' with description '" + desc + "'. This is not an event.");
                    }
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmavataroverride"), Summary("Changes the avatar to be a image from a link. - FMBot Admins only")]
        [Alias("fmsetavatar")]
        public async Task fmavataroverrideAsync(string link, string desc = "Custom FMBot Avatar", int ievent = 0)
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                if (link == "help")
                {
                    await ReplyAsync(cfgjson.CommandPrefix + "fmavataroverride <image link> [message in quotation marks] [event 0 or 1]");
                    return;
                }

                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;

                    if (ievent == 1)
                    {
                        _timer.UseCustomAvatarFromLink(client, link, desc, true);
                        await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
                    }
                    else
                    {
                        _timer.UseCustomAvatarFromLink(client, link, desc, false);
                        await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is not an event.");
                    }
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmnameoverride"), Summary("Changes the bot's name. - FMBot Owners only")]
        [Alias("fmsetbotname")]
        public async Task fmnameoverrideAsync(string name = ".fmbot")
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Owner))
            {
                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    await client.CurrentUser.ModifyAsync(u => u.Username = name);
                    await ReplyAsync("Set name to '" + name + "'");
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("Unable to set the name of the bot due to an internal error.");
                }
            }
        }

        [Command("fmresetavatar"), Summary("Changes the avatar to be the default. - FMBot Admins only")]
        public async Task fmresetavatar()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    _timer.UseDefaultAvatar(client);
                    await ReplyAsync("Set avatar to 'FMBot Default'");
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmrestarttimer"), Summary("Restarts the internal bot avatar timer. - FMBot Admins only")]
        [Alias("fmstarttimer", "fmtimerstart", "fmtimerrestart")]
        public async Task fmrestarttimerAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                try
                {
                    _timer.Restart();
                    await ReplyAsync("Timer restarted");
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmstoptimer"), Summary("Stops the internal bot avatar timer. - FMBot Admins only")]
        [Alias("fmtimerstop")]
        public async Task fmstoptimerAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                try
                {
                    _timer.Stop();
                    await ReplyAsync("Timer stopped");

                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmtimerstatus"), Summary("Checks the status of the timer. - FMBot Admins only")]
        public async Task fmtimerstatusAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                try
                {
                    if (_timer.IsTimerActive() == true)
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
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmserverlist"), Summary("Displays a list showing information related to every server the bot has joined. - FMBot Owners only")]
        public async Task fmserverlistAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Owner))
            {
                DiscordSocketClient SelfUser = Context.Client as DiscordSocketClient;

                string desc = null;

                foreach (SocketGuild guild in SelfUser.Guilds)
                {
                    desc += $"{guild.Name} - Users: {guild.Users.Count()}, Owner: {guild.Owner.ToString()}\n";
                }

                if (!string.IsNullOrWhiteSpace(desc))
                {
                    string[] descChunks = desc.SplitByMessageLength().ToArray();
                    foreach (string chunk in descChunks)
                    {
                        await Context.User.SendMessageAsync(chunk);
                    }
                }

                await Context.Channel.SendMessageAsync("Check your DMs!");
            }
        }

        [Command("fmremovereadonly"), Summary("Removes read only on all directories. - FMBot Owners only")]
        [Alias("fmreadonlyfix")]
        public async Task fmremovereadonlyAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Owner))
            {
                try
                {
                    if (Directory.Exists(GlobalVars.CacheFolder))
                    {
                        DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                        GlobalVars.ClearReadOnly(users);
                    }


                    await ReplyAsync("Removed read only on all directories.");
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("Unable to remove read only on all directories due to an internal error.");
                }
            }
        }

        [Command("fmstoragecheck"), Summary("Checks how much storage is left on the server. - FMBot Owners only")]
        [Alias("fmcheckstorage", "fmstorage")]
        public async Task fmstoragecheckAsync()
        {
            if (await adminService.HasCommandAccessAsync(Context.User, UserType.Owner))
            {
                try
                {
                    DriveInfo[] drives = DriveInfo.GetDrives();

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithDescription("Server Drive Info");

                    foreach (DriveInfo drive in drives.Where(w => w.IsReady))
                    {
                        builder.AddField(drive.Name + " - " + drive.VolumeLabel + ":", adminService.FormatBytes(drive.AvailableFreeSpace) + " free of " + adminService.FormatBytes(drive.TotalSize));
                    }

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("Unable to delete server drive info due to an internal error.");
                }
            }
        }

        #endregion

        #region FMBot Staff and Server Staff Only Commands

        [Command("fmblacklistadd"), Summary("Adds a user to a serverside blacklist - FMBot Admins and Server Staff only")]
        public async Task fmblacklistaddAsync(SocketGuildUser user = null)
        {
            try
            {
                if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
                {
                    if (user == null)
                    {
                        await ReplyAsync("Please specify what user you want to add to the blacklist.");
                        return;
                    }
                    else if (user == Context.Message.Author)
                    {
                        await ReplyAsync("You cannot blacklist yourself!");
                        return;
                    }
                   

                    string UserID = user.Id.ToString();

                    bool blacklistresult = await adminService.AddUserToBlacklistAsync(user.Id.ToString());

                    if (blacklistresult == true)
                    {
                        await ReplyAsync("Added " + user.Username + " to the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already added " + user.Username + " to the blacklist or the blacklist does not exist for this user.");
                    }
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient client = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(client, e);

                await ReplyAsync("Unable to add " + user.Username + " to the blacklist due to an internal error.");
            }
        }

        [Command("fmblacklistremove"), Summary("Removes a user from a serverside blacklist - FMBot Admins and Server Staff only")]
        public async Task fmblacklistremoveAsync(SocketGuildUser user = null)
        {
            try
            {
                if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
                {
                    if (user == null)
                    {
                        await ReplyAsync("Please specify what user you want to remove from the blacklist.");
                        return;
                    }

                    string UserID = user.Id.ToString();

                    bool blacklistresult = await adminService.RemoveUserFromBlacklistAsync(user.Id.ToString());

                    if (blacklistresult == true)
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
                DiscordSocketClient client = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(client, e);

                await ReplyAsync("Unable to remove " + user.Username + " from the blacklist due to an internal error.");
            }
        }

        #endregion
    }
}
