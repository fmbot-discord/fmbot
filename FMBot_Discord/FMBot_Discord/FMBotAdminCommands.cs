
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotAdminCommands : ModuleBase
    {
        //OwnerIDs = Bitl, Tytygigas, Opus v84, [opti], GreystarMusic, Lemonadeaholic, LantaarnAppel

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

        [Command("dbcheck"), Summary("Checks if an entry is in the database.")]
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
                    await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Offline);
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
            if (FMBotAdminUtil.IsOwner(DiscordUser))
            {
                await ReplyAsync("Restarting bot...");
                await (Context.Client as DiscordSocketClient).SetStatusAsync(UserStatus.Offline);
                Environment.Exit(1);
            }
        }

        [Command("fmadmin")]
        [Alias("fmadminhelp")]
        public async Task AdminAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (FMBotAdminUtil.IsAdmin(DiscordUser))
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                var SelfName = Context.Client.CurrentUser;
                string prefix = cfgjson.CommandPrefix;

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = SelfName.GetAvatarUrl();
                eab.Name = SelfName.Username;

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);

                foreach (var module in _service.Modules)
                {
                    if (module.Name.Equals("FMBotCommands"))
                    {
                        continue;
                    }

                    string description = null;
                    foreach (var cmd in module.Commands)
                    {
                        var result = await cmd.CheckPreconditionsAsync(Context);
                        if (result.IsSuccess)
                            description += $"{prefix}{cmd.Aliases.First()}\n";
                    }

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        builder.AddField(x =>
                        {
                            x.Name = "Admin Commands";
                            x.Value = description;
                            x.IsInline = false;
                        });
                    }
                }

                await ReplyAsync("", false, builder.Build());
            }
        }
    }
}
