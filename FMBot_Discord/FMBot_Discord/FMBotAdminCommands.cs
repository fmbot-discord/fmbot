
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotModules;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotAdminCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly TimerService _timer;

        public FMBotAdminCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }

        [Command("announce"), Summary("Sends an announcement to the main server. - FMBot Admin only")]
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
                        await ReplyAsync("You do not have access to this function of the command.");
                    }
                    else if (channel != null && FMBotAdminUtil.HasCommandAccess(DiscordUser, 1) && !DiscordUser.GuildPermissions.KickMembers)
                    {
                        await ReplyAsync("You do not have access to this function of the command.");
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

        [Command("dbcheck"), Summary("Checks if an entry is in the database. - FMBot Admin Only")]
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

        [Command("fmserverreboot"), Summary("Reboots the Vultr VPS server. - FMBot Owner only")]
        [Alias("fmreboot")]
        public async Task fmserverrebootAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                string Data = "SUBID=" + cfgjson.VultrSubID;
                string Reponse = "";
                StreamWriter Sw = null;
                StreamReader Sr = null;
                try
                {
                    await ReplyAsync("Rebooting server...");
                    await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                    HttpWebRequest Req = (HttpWebRequest)WebRequest.Create("https://api.vultr.com/v1/server/reboot");
                    Req.Method = "POST";
                    Req.ContentType = "application/x-www-form-urlencoded";
                    Req.Headers.Add("API-Key: " + cfgjson.VultrKey);
                    using (var sw = new StreamWriter(Req.GetRequestStream()))
                    {
                        sw.Write(Data);
                    }
                    Sr = new
                    StreamReader(((HttpWebResponse)Req.GetResponse()).GetResponseStream());
                    Reponse = Sr.ReadToEnd();
                    Sr.Close();
                }
                catch (Exception ex)
                {
                    if (Sw != null)
                        Sw.Close();
                    if (Sr != null)
                        Sr.Close();

                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(client, ex);

                    await ReplyAsync("Error rebooting server. Look in bot log.");
                    await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "FMBotAdminCommands - fmserverreboot", "Vultr API Error", ex));
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

        [Command("fmsetperms"), Summary("Gives permissions to other users. - Owner only")]
        public async Task fmsetpermsAsync(IUser user = null, int permtype = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.HasCommandAccess(DiscordUser, 3))
            {
                var ChosenUser = user ?? Context.Message.Author;
                string UserID = ChosenUser.Id.ToString();
                string LastFMName = DBase.GetNameForID(UserID);
                int LastFMMode = DBase.GetModeIntForID(UserID);
                if (!LastFMName.Equals("NULL"))
                {
                    DBase.WriteEntry(UserID, LastFMName, LastFMMode, permtype);

                    if (permtype == 1)
                    {
                        await ReplyAsync("The user now has Admin permissions");
                    }
                    else if (permtype == 2)
                    {
                        await ReplyAsync("The user now has Super Admin permissions");
                    }
                    else
                    {
                        await ReplyAsync("The user now has User permissions");
                    }
                }
                else
                {
                    await ReplyAsync("The user's Last.FM name has not been set.");
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

        [Command("fmblacklistadd"), Summary("Adds a user to a serverside blacklist - FMBot Admins and Server Admins/Mods only")]
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
                    }
                    else if (user == Context.Message.Author)
                    {
                        await ReplyAsync("You cannot blacklist yourself!");
                    }
                    else if (user.Id == user.Guild.OwnerId)
                    {
                        await ReplyAsync("You cannot blacklist the owner!");
                    }
                    else if (FMBotAdminUtil.IsRankAbove(Context.Message.Author, user))
                    {
                        await ReplyAsync("You cannot blacklist someone who has a higher rank than you!");
                    }

                    string UserID = user.Id.ToString();
                    string ServerID = user.Guild.Id.ToString();

                    bool blacklistresult = DBase.AddToBlacklist(UserID, ServerID);

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
                    await ReplyAsync("You must have the 'Ban Members' permission in order to use this command.");
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

        [Command("fmblacklistremove"), Summary("Removes a user from a serverside blacklist - Server Admins/Mods only")]
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
                    }
                    else if (user == Context.Message.Author)
                    {
                        await ReplyAsync("You cannot remove yourself!");
                    }
                    else if (user.Id == user.Guild.OwnerId)
                    {
                        await ReplyAsync("You cannot remove the owner from the blacklist!");
                    }
                    else if (FMBotAdminUtil.IsRankAbove(Context.Message.Author, user))
                    {
                        await ReplyAsync("You cannot blacklist someone who has a higher rank than you!");
                    }

                    string UserID = user.Id.ToString();
                    string ServerID = user.Guild.Id.ToString();

                    bool blacklistresult = DBase.RemoveFromBlacklist(UserID, ServerID);

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
                    await ReplyAsync("You must have the 'Ban Members' permission in order to use this command.");
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
    }
}
