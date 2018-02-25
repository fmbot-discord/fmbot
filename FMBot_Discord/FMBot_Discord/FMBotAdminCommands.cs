
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
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

        [Command("announce"), Summary("Sends an announcement to the main server. - Admin only")]
        [Alias("fmannounce", "fmnews", "news", "fmannouncement", "announcement")]
        public async Task announceAsync(string message, string ThumbnailURL = null)
        {
            var cfgjson = await JsonCfg.GetJSONDataAsync();

            if (message == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "announce <message> [image url]");
                return;
            }

            var DiscordUser = (IGuildUser)Context.Message.Author;
            var SelfUser = Context.Client.CurrentUser;
            try
            {
                ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.AnnouncementChannel);
                ITextChannel channel = await Context.Guild.GetTextChannelAsync(BroadcastChannelID);
                if (FMBotAdminUtil.IsAdmin(DiscordUser))
                {
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
                    catch (Exception)
                    {
                    }

                    builder.AddField("Announcement", message);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("The announcement channel has not been set.");
            }
        }

        [Command("dbcheck"), Summary("Checks if an entry is in the database. - Admin Only")]
        public async Task dbcheckAsync(IUser user = null)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsAdmin(DiscordUser))
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

        [Command("fmserverreboot"), Summary("Reboots the Vultr VPS server. - Owner only")]
        [Alias("fmreboot")]
        public async Task fmserverrebootAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsOwner(DiscordUser))
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

                    await ReplyAsync("Error rebooting server. Look in bot console.");
                    Console.WriteLine("Vultr API Error: " + ex.Message);
                }
            }
        }

        [Command("fmbotrestart"), Summary("Reboots the bot. - Super Admins only")]
        [Alias("fmrestart")]
        public async Task fmbotrestartAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
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
            if (FMBotAdminUtil.IsOwner(DiscordUser))
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

        [Command("fmalbumoverride"), Summary("Changes the avatar to be an album. - Super Admins only")]
        public async Task fmalbumoverrideAsync(string albumname, string desc, int ievent = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
            {
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
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmartistoverride"), Summary("Changes the avatar to be an artist. - Super Admins only")]
        public async Task fmartistoverrideAsync(string artistname, string desc, int ievent = 0)
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
            {
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
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmresetavatar"), Summary("Changes the avatar to be the default. - Super Admins only")]
        public async Task fmresetavatar()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
            {
                try
                {
                    DiscordSocketClient client = Context.Client as DiscordSocketClient;
                    _timer.UseDefaultAvatar(client);
                    await ReplyAsync("Set avatar to 'FMBot Default'");
                }
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmrestarttimer"), Summary("Restarts the internal bot avatar timer. - Super Admins only")]
        [Alias("fmstarttimer", "fmtimerstart")]
        public async Task fmrestarttimerAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
            {
                try
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
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmstoptimer"), Summary("Stops the internal bot avatar timer. - Owner only")]
        public async Task fmstoptimerAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsOwner(DiscordUser))
            {
                try
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
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }

        [Command("fmtimerstatus"), Summary("Checks the status of the timer. - Admin only")]
        public async Task fmtimerstatusAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                catch (Exception)
                {
                    await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
                }
            }
        }
    }
}
