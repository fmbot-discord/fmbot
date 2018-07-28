using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotModules;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotAdminCommands : ModuleBase
    {
        #region Constructor

        private readonly CommandService _service;
        private readonly TimerService _timer;

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
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1))
            {
                var ChosenUser = user ?? Context.Message.Author;
                string LastFMName = DBase.GetNameForID(ChosenUser.Id.ToString());
                string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(ChosenUser.Id.ToString()));
                if (!LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("The user's Last.FM name is '" + LastFMName + "'. Their mode is set to '" + LastFMMode + "'.");
                }
                else
                {
                    await ReplyAsync("The user's Last.FM name has not been set.");
                }
            }
        }

        [Command("fmbotrestart"), Summary("Reboots the bot. - FMBot Super Admins only")]
        [Alias("fmrestart")]
        public async Task fmbotrestartAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                await ReplyAsync("Restarting bot...");
                await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                Environment.Exit(1);
            }
        }

        [Command("fmsetperms"), Summary("Gives permissions to other users. - FMBot Owners only")]
        public async Task fmsetpermsAsync(IUser user = null, int permtype = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
            {
                var ChosenUser = user ?? Context.Message.Author;
                string UserID = ChosenUser.Id.ToString();
                if (permtype == 1)
                {
                    DBase.WriteAdminEntry(UserID, permtype);
                    await ReplyAsync("The user now has Admin permissions");
                }
                else if (permtype == 2)
                {
                    DBase.WriteAdminEntry(UserID, permtype);
                    await ReplyAsync("The user now has Super Admin permissions");
                }
                else if (permtype == 3)
                {
                    if (FMBotAdminUtil.IsSoleOwner(DiscordUser))
                    {
                        DBase.WriteAdminEntry(UserID, permtype);
                        await ReplyAsync("The user now has Owner permissions");
                    }
                    else
                    {
                        await ReplyAsync("You cannot promote a user to Owner");
                    }
                }
                else
                {
                    DBase.RemoveAdminEntry(UserID);
                    await ReplyAsync("The user now has User permissions");
                }
            }
        }

        [Command("fmalbumoverride"), Summary("Changes the avatar to be an album. - FMBot Super Admins only")]
        [Alias("fmsetalbum")]
        public async Task fmalbumoverrideAsync(string albumname, string desc = "Custom FMBot Album Avatar", int ievent = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

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

        [Command("fmartistoverride"), Summary("Changes the avatar to be an artist. - FMBot Super Admins only")]
        [Alias("fmsetartist")]
        public async Task fmartistoverrideAsync(string artistname, string desc = "Custom FMBot Artist Avatar", int ievent = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

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

        [Command("fmavataroverride"), Summary("Changes the avatar to be a image from a link. - FMBot Super Admins only")]
        [Alias("fmsetavatar")]
        public async Task fmavataroverrideAsync(string link, string desc = "Custom FMBot Avatar", int ievent = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

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
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
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

        [Command("fmresetavatar"), Summary("Changes the avatar to be the default. - FMBot Super Admins only")]
        public async Task fmresetavatar()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
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

        [Command("fmrestarttimer"), Summary("Restarts the internal bot avatar timer. - FMBot Super Admins only")]
        [Alias("fmstarttimer", "fmtimerstart", "fmtimerrestart")]
        public async Task fmrestarttimerAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                try
                {
                    if (!FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        if (_timer.IsTimerActive() == false)
                        {
                            _timer.Restart();
                            await ReplyAsync("Timer restarted");
                        }
                        else
                        {
                            await ReplyAsync("The timer is already active!");
                        }
                    }
                    else
                    {
                        _timer.Restart();
                        await ReplyAsync("Timer restarted");
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

        [Command("fmstoptimer"), Summary("Stops the internal bot avatar timer. - FMBot Super Admins only")]
        [Alias("fmtimerstop")]
        public async Task fmstoptimerAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 2))
            {
                try
                {
                    if (!FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        if (_timer.IsTimerActive() == true)
                        {
                            _timer.Stop();
                            await ReplyAsync("Timer stopped");
                        }
                        else
                        {
                            await ReplyAsync("The timer has already stopped!");
                        }
                    }
                    else
                    {
                        _timer.Stop();
                        await ReplyAsync("Timer stopped");
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

        [Command("fmtimerstatus"), Summary("Checks the status of the timer. - FMBot Admins only")]
        public async Task fmtimerstatusAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1))
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

        [Command("fmserverlist"), Summary("Displays a list showing information related to every server the bot has joined. - FMBot Admins only")]
        public async Task fmserverlistAsync()
        {
            var DiscordUser = Context.Message.Author;

            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1))
            {
                var SelfUser = Context.Client as DiscordSocketClient;

                string desc = null;

                foreach (var guild in SelfUser.Guilds)
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

        [Command("fmdeletechartcache"), Summary("Deletes all chart images. - FMBot Owners only")]
        [Alias("fmdeletecharts")]
        public async Task fmdeletechartsAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
            {
                try
                {
                    DBase.DeleteAllCharts();
                    await ReplyAsync("Deleted all chart images.");
                }
                catch (Exception e)
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, e);
                    await ReplyAsync("Unable to delete all chart images due to an internal error.");
                }
            }
        }

        [Command("fmremovereadonly"), Summary("Removes read only on all directories. - FMBot Owners only")]
        [Alias("fmreadonlyfix")]
        public async Task fmremovereadonlyAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
            {
                try
                {
                    if (Directory.Exists(GlobalVars.UsersFolder))
                    {
                        var users = new DirectoryInfo(GlobalVars.UsersFolder);
                        GlobalVars.ClearReadOnly(users);
                    }

                    if (Directory.Exists(GlobalVars.ServersFolder))
                    {
                        var servers = new DirectoryInfo(GlobalVars.ServersFolder);
                        GlobalVars.ClearReadOnly(servers);
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

        #endregion

        #region Server Staff Only Commands

        [Command("fmserverset"), Summary("Sets the global FMBot settings for the server. - Server Staff only")]
        [Alias("fmserversetmode")]
        public async Task fmserversetAsync([Summary("The mode you want to use.")] string mode = "userdefined")
        {
            var ServerUser = (IGuildUser)Context.Message.Author;

            if (ServerUser.GuildPermissions.BanMembers)
            {
                if (mode == "help")
                {
                    var cfgjson = await JsonCfg.GetJSONDataAsync();
                    await ReplyAsync(cfgjson.CommandPrefix + "fmserverset [embedmini/embedfull/textfull/textmini/userdefined]");
                    return;
                }

                string GuildID = ServerUser.GuildId.ToString();
                var SelfUser = Context.Client.CurrentUser;
                if (DBase.ServerEntryExists(GuildID))
                {
                    int modeint = 0;

                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        modeint = DBase.GetIntForModeName(mode);
                        if (modeint > 4 || modeint < 0)
                        {
                            await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', 'textmini', or 'userdefined'.");
                            return;
                        }
                    }
                    else
                    {
                        modeint = DBase.GetModeIntForServerID(GuildID);
                    }

                    DBase.WriteServerEntry(GuildID, modeint);
                }
                else
                {
                    int modeint = DBase.GetIntForModeName("embedmini");
                    DBase.WriteServerEntry(GuildID, modeint);
                }
                string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForServerID(GuildID), true);
                await ReplyAsync("The global " + SelfUser.Username + " mode has been set to '" + LastFMMode + "'.");
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission can use this command.");
            }
        }

        #endregion

        #region FMBot Staff and Server Staff Only Commands

        [Command("announce"), Summary("Sends an announcement to the main server or a channel. - FMBot Admins and Server Staff only")]
        [Alias("fmannounce", "fmnews", "news", "fmannouncement", "announcement")]
        public async Task announceAsync(string message, string ThumbnailURL = null, ITextChannel channel = null)
        {
            var cfgjson = await JsonCfg.GetJSONDataAsync();

            var DiscordUser = (IGuildUser)Context.Message.Author;
            var SelfUser = Context.Client.CurrentUser;
            try
            {
                if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) || DiscordUser.GuildPermissions.KickMembers)
                {
                    if (message == "help")
                    {
                        await ReplyAsync(cfgjson.CommandPrefix + "announce <message in quotation marks> [image url] [channel id (only available for server mods/admins)]");
                        return;
                    }
                }

                ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.AnnouncementChannel);
                ITextChannel customchannel = null;
                if (channel == null)
                {
                    customchannel = await Context.Guild.GetTextChannelAsync(BroadcastChannelID);
                }
                else
                {
                    customchannel = await Context.Guild.GetTextChannelAsync(channel.Id);
                }

                if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) || DiscordUser.GuildPermissions.KickMembers)
                {
                    if (channel == null && !FMBotAdminUtil.HasCommandAccess(DiscordUser, 1))
                    {
                        await ReplyAsync("You are not authorized to use this command. Only users with the 'Kick Members' permission can use this command.");
                    }
                    else if (channel != null && FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) && !DiscordUser.GuildPermissions.KickMembers)
                    {
                        await ReplyAsync("You are not authorized to use this command.");
                    }

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname + " (" + DiscordUser.Username + ")";
                    }

                    var builder = new EmbedBuilder();
                    builder.WithAuthor(eab);

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(ThumbnailURL))
                        {
                            builder.WithThumbnailUrl(ThumbnailURL);
                        }
                        else
                        {
                            builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                        }
                    }
                    catch (Exception e)
                    {
                        DiscordSocketClient client = Context.Client as DiscordSocketClient;
                        ExceptionReporter.ReportException(client, e);
                    }

                    builder.AddField("Announcement", message);

                    await customchannel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient client = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(client, e);
                await ReplyAsync("The announcement channel has not been set.");
            }
        }

        [Command("fmblacklistadd"), Summary("Adds a user to a serverside blacklist - FMBot Admins and Server Staff only")]
        public async Task fmblacklistaddAsync(SocketGuildUser user = null)
        {
            try
            {
                var DiscordUser = Context.Message.Author as SocketGuildUser;

                if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) || DiscordUser.GuildPermissions.BanMembers)
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
                    else if (user.Id == user.Guild.OwnerId)
                    {
                        await ReplyAsync("You cannot blacklist the owner!");
                        return;
                    }
                    else if (FMBotAdminUtil.IsRankAbove(Context.Message.Author, user))
                    {
                        await ReplyAsync("You cannot blacklist someone who has a higher rank than you!");
                        return;
                    }

                    string UserID = user.Id.ToString();
                    string ServerID = user.Guild.Id.ToString();

                    bool blacklistresult = DBase.AddToBlacklist(ServerID, UserID);

                    if (blacklistresult == true)
                    {
                        if (string.IsNullOrWhiteSpace(user.Nickname))
                        {
                            await ReplyAsync("Added " + user.Username + " to the blacklist.");
                        }
                        else
                        {
                            await ReplyAsync("Added " + user.Nickname + " (" + user.Username + ") to the blacklist.");
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(user.Nickname))
                        {
                            await ReplyAsync("You have already added " + user.Username + " to the blacklist or the blacklist does not exist for this user.");
                        }
                        else
                        {
                            await ReplyAsync("You have already added " + user.Nickname + " (" + user.Username + ") to the blacklist or the blacklist does not exist for this user.");
                        }
                    }
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission can use this command.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient client = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(client, e);

                if (string.IsNullOrWhiteSpace(user.Nickname))
                {
                    await ReplyAsync("Unable to add " + user.Username + " to the blacklist due to an internal error.");
                }
                else
                {
                    await ReplyAsync("Unable to add " + user.Nickname + " (" + user.Username + ") to the blacklist due to an internal error.");
                }
            }
        }

        [Command("fmblacklistremove"), Summary("Removes a user from a serverside blacklist - FMBot Admins and Server Staff only")]
        public async Task fmblacklistremoveAsync(SocketGuildUser user = null)
        {
            try
            {
                var DiscordUser = Context.Message.Author as SocketGuildUser;

                if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) || DiscordUser.GuildPermissions.BanMembers)
                {
                    if (user == null)
                    {
                        await ReplyAsync("Please specify what user you want to remove from the blacklist.");
                        return;
                    }
                    else if (user == Context.Message.Author)
                    {
                        await ReplyAsync("You cannot remove yourself!");
                        return;
                    }
                    else if (user.Id == user.Guild.OwnerId)
                    {
                        await ReplyAsync("You cannot remove the owner from the blacklist!");
                        return;
                    }
                    else if (FMBotAdminUtil.IsRankAbove(Context.Message.Author, user))
                    {
                        await ReplyAsync("You cannot blacklist someone who has a higher rank than you!");
                        return;
                    }

                    string UserID = user.Id.ToString();
                    string ServerID = user.Guild.Id.ToString();

                    bool blacklistresult = DBase.RemoveFromBlacklist(ServerID, UserID);

                    if (blacklistresult == true)
                    {
                        if (string.IsNullOrWhiteSpace(user.Nickname))
                        {
                            await ReplyAsync("Removed " + user.Username + " from the blacklist.");
                        }
                        else
                        {
                            await ReplyAsync("Removed " + user.Nickname + " (" + user.Username + ") from the blacklist.");
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(user.Nickname))
                        {
                            await ReplyAsync("You have already removed " + user.Username + " from the blacklist.");
                        }
                        else
                        {
                            await ReplyAsync("You have already removed " + user.Nickname + " (" + user.Username + ") from the blacklist.");
                        }
                    }
                }
                else
                {
                    await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission can use this command.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient client = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(client, e);

                if (string.IsNullOrWhiteSpace(user.Nickname))
                {
                    await ReplyAsync("Unable to remove " + user.Username + " from the blacklist due to an internal error.");
                }
                else
                {
                    await ReplyAsync("Unable to remove " + user.Nickname + " (" + user.Username + ") from the blacklist due to an internal error.");
                }
            }
        }

        #endregion
    }
}
