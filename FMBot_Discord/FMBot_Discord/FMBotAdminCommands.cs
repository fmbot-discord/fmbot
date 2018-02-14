
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotAdminCommands : ModuleBase
    {
        private readonly CommandService _service;

        public FMBotAdminCommands(CommandService service)
        {
            _service = service;
        }

        [Command("announce"), Summary("Sends an announcement to the main server.")]
        [Alias("fmannounce", "fmnews", "news", "fmannouncement", "announcement")]
        public async Task announceAsync(string message, string ThumbnailURL = null)
        {
            var cfgjson = await JsonCfg.GetJSONDataAsync();

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

        [Command("fmserverreboot")]
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

        [Command("fmbotrestart")]
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

        [Command("fmbotshutdown")]
        [Alias("fmshutdown")]
        public async Task fmbotshutdownAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsOwner(DiscordUser))
            {
                await ReplyAsync("Shutting down bot...");
                await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                Environment.Exit(0);
            }
        }

        [Command("fmservershutdown")]
        [Alias("fmhardshutdown", "fmhalt", "fmserverhalt")]
        public async Task fmservershutdownAsync()
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
                    await ReplyAsync("Shutting down server...");
                    await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Invisible);
                    HttpWebRequest Req = (HttpWebRequest)WebRequest.Create("https://api.vultr.com/v1/server/halt");
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

        [Command("fmsetperms")]
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
    }
}
