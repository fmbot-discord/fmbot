using Discord;
using Discord.Commands;
using Discord.WebSocket;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Bot
{
    public class FMBotUtil
    {
        #region Database Functions

        public class DBase
        {
            #region User Settings

            public static string FMAdminString = "IsAdmin";
            public static string FMSuperAdminString = "IsSuperAdmin";
            public static string FMOwnerString = "IsOwner";

            public static void WriteEntry(string id, string name, int fmval = 0)
            {
                File.WriteAllText(GlobalVars.CacheFolder + id + ".txt", name + Environment.NewLine + fmval.ToString());
                File.SetAttributes(GlobalVars.CacheFolder + id + ".txt", FileAttributes.Normal);
            }

            public static void RemoveEntry(string id)
            {
                if (EntryExists(id))
                {
                    File.SetAttributes(GlobalVars.CacheFolder + id + ".txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.CacheFolder + id + ".txt");
                }
                if (FriendsExists(id))
                {
                    File.SetAttributes(GlobalVars.CacheFolder + id + "-friends.txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.CacheFolder + id + "-friends.txt");
                }
                if (File.Exists(GlobalVars.CacheFolder + id + "-chart.png"))
                {
                    File.SetAttributes(GlobalVars.CacheFolder + id + "-chart.png", FileAttributes.Normal);
                    File.Delete(GlobalVars.CacheFolder + id + "-chart.png");
                }
            }

            public async static Task<IGuildUser> ConvertIDToGuildUser(IGuild guild, ulong id)
            {
                var users = await guild.GetUsersAsync();

                foreach (var user in users)
                {
                    if (user.Id == id)
                    {
                        return user;
                    }
                }

                return null;
            }

            public async static Task<IGuildUser> GetIGuildUserForFMName(IGuild guild, string name)
            {
                ulong FMNameID = GetIDForName(name);

                IGuildUser user = await ConvertIDToGuildUser(guild, FMNameID);

                if (user.Id == FMNameID)
                {
                    return user;
                }
                else
                {
                    return null;
                }
            }

            public static bool EntryExists(string id)
            {
                return File.Exists(GlobalVars.CacheFolder + id + ".txt");
            }

            public static string GetNameForID(string id)
            {
                string line;

                using (StreamReader file = new StreamReader(GlobalVars.CacheFolder + id + ".txt"))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        file.Close();
                        return line;
                    }
                }

                return "NULL";
            }

            public static ulong GetIDForName(string name)
            {
                foreach (FileData file in FastDirectoryEnumerator.EnumerateFiles(GlobalVars.CacheFolder, "*.txt"))
                {
                    if (File.ReadAllText(file.Path).Contains(name))
                    {
                        string nameConvert = Path.GetFileNameWithoutExtension(file.Path);
                        ulong DiscordID = Convert.ToUInt64(nameConvert);
                        return DiscordID;
                    }
                }

                return 0;
            }

            public static string GetRandFMName()
            {
                Random rand = new Random();
                List<FileData> files = FastDirectoryEnumerator.EnumerateFiles(GlobalVars.CacheFolder).Where(F => F.Name.ToLower().EndsWith(".txt")).ToList();
                FileData randomFile = files[rand.Next(0, files.Count)];

                string line;

                using (StreamReader file = new StreamReader(randomFile.Path))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        file.Close();
                        return line;
                    }
                }

                return "NULL";
            }

            public static string GetFMChartForID(string id)
            {
                string filename = GlobalVars.CacheFolder + id + "-chart.png";

                if (File.Exists(filename))
                {
                    return filename;
                }
                else
                {
                    return "NULL";
                }
            }

            public static string GetRandomFMChart()
            {
                Random rand = new Random();
                List<FileData> files = FastDirectoryEnumerator.EnumerateFiles(GlobalVars.CacheFolder).Where(F => F.Name.ToLower().EndsWith(".png")).ToList();
                FileData randomFile = files[rand.Next(0, files.Count)];

                if (File.Exists(randomFile.Path))
                {
                    return randomFile.Path;
                }
                else
                {
                    return "NULL";
                }
            }

            public static ulong GetIDFromChart(string chartname)
            {
                if (!string.IsNullOrWhiteSpace(chartname))
                {
                    string fileName = Path.GetFileName(chartname);
                    string IDString = fileName.Replace("-chart.png", "");
                    ulong ID = Convert.ToUInt64(IDString);
                    return ID;
                }
                else
                {
                    return 0;
                }
            }

            public static void DeleteAllCharts()
            {
                foreach (FileData file in FastDirectoryEnumerator.EnumerateFiles(GlobalVars.CacheFolder, "*.png"))
                {
                    File.Delete(file.Path);
                }
            }

            public static int GetModeIntForID(string id)
            {
                string line;

                using (StreamReader file = new StreamReader(GlobalVars.CacheFolder + id + ".txt"))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        string nextline = file.ReadLine();
                        file.Close();
                        return Convert.ToInt32(nextline);
                    }
                }

                return 4;
            }
            #endregion

            #region Admin Settings

            public static void WriteAdminEntry(string id, int IsAdmin = 0)
            {
                string AdminString = "";

                if (IsAdmin == 1)
                {
                    AdminString = FMAdminString;
                }
                else if (IsAdmin == 2)
                {
                    AdminString = FMSuperAdminString;
                }
                else if (IsAdmin == 3)
                {
                    AdminString = FMOwnerString;
                }

                File.WriteAllText(GlobalVars.CacheFolder + id + "-adminsettings.txt", AdminString);
                File.SetAttributes(GlobalVars.CacheFolder + id + "-adminsettings.txt", FileAttributes.Normal);
            }

            public static void RemoveAdminEntry(string id)
            {
                if (AdminEntryExists(id))
                {
                    File.SetAttributes(GlobalVars.CacheFolder + id + "-adminsettings.txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.CacheFolder + id + "-adminsettings.txt");
                }
            }

            public static bool AdminEntryExists(string id)
            {
                return File.Exists(GlobalVars.CacheFolder + id + "-adminsettings.txt");
            }

            public static bool CheckAdmin(string id)
            {
                if (AdminEntryExists(id))
                {
                    if (File.ReadLines(GlobalVars.CacheFolder + id + "-adminsettings.txt").Any(line => line.Contains(FMAdminString)))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            public static bool CheckSuperAdmin(string id)
            {
                if (AdminEntryExists(id))
                {
                    if (File.ReadLines(GlobalVars.CacheFolder + id + "-adminsettings.txt").Any(line => line.Contains(FMSuperAdminString)))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            public static bool CheckOwner(string id)
            {
                if (AdminEntryExists(id))
                {
                    if (File.ReadLines(GlobalVars.CacheFolder + id + "-adminsettings.txt").Any(line => line.Contains(FMOwnerString)))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            public static string GetAdminStringFromID(string id)
            {
                if (CheckAdmin(id))
                {
                    return FMAdminString;
                }
                else if (CheckSuperAdmin(id))
                {
                    return FMSuperAdminString;
                }
                else if (CheckOwner(id))
                {
                    return FMOwnerString;
                }
                else
                {
                    return "";
                }
            }

            public static int GetAdminIntFromID(string id)
            {
                if (CheckAdmin(id))
                {
                    return 1;
                }
                else if (CheckSuperAdmin(id))
                {
                    return 2;
                }
                else if (CheckOwner(id))
                {
                    return 3;
                }
                else
                {
                    return 0;
                }
            }

            #endregion

            #region Server Settings

            public static void WriteServerEntry(string id, int globalFMMode = 4)
            {
                File.WriteAllText(GlobalVars.ServersFolder + id + ".txt", globalFMMode.ToString());
                File.SetAttributes(GlobalVars.ServersFolder + id + ".txt", FileAttributes.Normal);
            }

            public static void RemoveServerEntry(string id)
            {
                if (File.Exists(GlobalVars.ServersFolder + id + ".txt"))
                {
                    File.SetAttributes(GlobalVars.ServersFolder + id + ".txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.ServersFolder + id + ".txt");
                }
            }

            public static int GetModeIntForServerID(string id)
            {
                string line;

                using (StreamReader file = new StreamReader(GlobalVars.ServersFolder + id + ".txt"))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        file.Close();
                        return Convert.ToInt32(line);
                    }
                }

                return 4;
            }

            public static bool ServerEntryExists(string id)
            {
                return File.Exists(GlobalVars.ServersFolder + id + ".txt");
            }

            #endregion

            #region Blacklist Settings

            public static bool BlacklistExists(string serverid)
            {
                return File.Exists(GlobalVars.ServersFolder + serverid + "-blacklist.txt");
            }

            public static bool AddToBlacklist(string serverid, string id)
            {
                if (!BlacklistExists(serverid))
                {
                    File.Create(GlobalVars.ServersFolder + serverid + "-blacklist.txt").Dispose();
                    File.SetAttributes(GlobalVars.ServersFolder + serverid + "-blacklist.txt", FileAttributes.Normal);
                }

                string[] blacklist = File.ReadAllLines(GlobalVars.ServersFolder + serverid + "-blacklist.txt");

                if (blacklist.Contains(id))
                {
                    return false;
                }

                var list = new List<string>(blacklist);
                list.Add(id);
                blacklist = list.ToArray();

                File.WriteAllLines(GlobalVars.ServersFolder + serverid + "-blacklist.txt", blacklist);
                File.SetAttributes(GlobalVars.ServersFolder + serverid + "-blacklist.txt", FileAttributes.Normal);

                return true;
            }

            public static bool RemoveFromBlacklist(string serverid, string id)
            {
                if (!BlacklistExists(serverid))
                {
                    return false;
                }

                string[] blacklist = File.ReadAllLines(GlobalVars.ServersFolder + serverid + "-blacklist.txt");

                if (blacklist.Contains(id))
                {
                    var list = new List<string>(blacklist);
                    list.Remove(id);
                    blacklist = list.ToArray();

                    File.WriteAllLines(GlobalVars.ServersFolder + serverid + "-blacklist.txt", blacklist);
                    File.SetAttributes(GlobalVars.ServersFolder + serverid + "-blacklist.txt", FileAttributes.Normal);

                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool IsUserOnBlacklist(string serverid, string id)
            {
                if (!BlacklistExists(serverid))
                {
                    return false;
                }

                string[] blacklist = File.ReadAllLines(GlobalVars.ServersFolder + serverid + "-blacklist.txt");

                if (blacklist.Contains(id))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            #endregion

            #region Friend List Settings

            public static bool FriendsExists(string id)
            {
                return File.Exists(GlobalVars.CacheFolder + id + "-friends.txt");
            }

            public static int AddFriendsEntry(string id, params string[] friendlist)
            {
                if (!FriendsExists(id))
                {
                    File.Create(GlobalVars.CacheFolder + id + "-friends.txt").Dispose();
                    File.SetAttributes(GlobalVars.CacheFolder + id + "-friends.txt", FileAttributes.Normal);
                }

                string[] friends = File.ReadAllLines(GlobalVars.CacheFolder + id + "-friends.txt");

                int listcount = friendlist.Count();

                var list = new List<string>(friends);

                foreach (var friend in friendlist)
                {
                    if (!friends.Contains(friend))
                    {
                        list.Add(friend);
                    }
                    else
                    {
                        listcount = listcount - 1;
                        continue;
                    }
                }

                friends = list.ToArray();
                
                File.WriteAllLines(GlobalVars.CacheFolder + id + "-friends.txt", friends);
                File.SetAttributes(GlobalVars.CacheFolder + id + "-friends.txt", FileAttributes.Normal);

                return listcount;
            }

            public static int RemoveFriendsEntry(string id, params string[] friendlist)
            {
                if (!FriendsExists(id))
                {
                    return 0;
                }

                string[] friends = File.ReadAllLines(GlobalVars.CacheFolder + id + "-friends.txt");
                var friendsLower = friends.Select(s => s.ToLowerInvariant()).ToArray();

                int listcount = friendlist.Count();

                var list = new List<string>(friends);


                foreach (var friend in friendlist)
                {
                    if (friendsLower.Contains(friend.ToLower()))
                    {
                        list.RemoveAll(n => n.Equals(friend, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        listcount = listcount - 1;
                        continue;
                    }
                }
                
                friends = list.ToArray();

                File.WriteAllLines(GlobalVars.CacheFolder + id + "-friends.txt", friends);
                File.SetAttributes(GlobalVars.CacheFolder + id + "-friends.txt", FileAttributes.Normal);

                return listcount;
            }

            public static string[] GetFriendsForID(string id)
            {
                string[] lines = File.ReadAllLines(GlobalVars.CacheFolder + id + "-friends.txt");
                return lines;
            }

            #endregion

            #region Global Settings

            public static int GetIntForModeName(string mode)
            {
                if (mode.Equals("embedmini"))
                {
                    return 0;
                }
                else if (mode.Equals("embedfull"))
                {
                    return 1;
                }
                else if (mode.Equals("textfull"))
                {
                    return 2;
                }
                else if (mode.Equals("textmini"))
                {
                    return 3;
                }
                else if (mode.Equals("userdefined"))
                {
                    return 4;
                }
                else
                {
                    return 4;
                }
            }

            public static string GetNameForModeInt(int mode, bool isservercmd = false)
            {
                if (mode == 0)
                {
                    return "embedmini";
                }
                else if (mode == 1)
                {
                    return "embedfull";
                }
                else if (mode == 2)
                {
                    return "textfull";
                }
                else if (mode == 3)
                {
                    return "textmini";
                }
                else if ((mode > 3 || mode < 0) && isservercmd == true)
                {
                    return "userdefined";
                }
                else
                {
                    return "NULL";
                }
            }

            #endregion
        }

        #endregion

        #region Configuration Data

        public class JsonCfg
        {
            // this structure will hold data from config.json
            public struct ConfigJson
            {
                [JsonProperty("token")]
                public string Token { get; private set; }

                [JsonProperty("fmkey")]
                public string FMKey { get; private set; }

                [JsonProperty("fmsecret")]
                public string FMSecret { get; private set; }

                [JsonProperty("prefix")]
                public string CommandPrefix { get; private set; }

                [JsonProperty("baseserver")]
                public string BaseServer { get; private set; }

                [JsonProperty("announcementchannel")]
                public string AnnouncementChannel { get; private set; }

                [JsonProperty("featuredchannel")]
                public string FeaturedChannel { get; private set; }

                [JsonProperty("botowner")]
                public string BotOwner { get; private set; }

                [JsonProperty("timerinit")]
                public string TimerInit { get; private set; }

                [JsonProperty("timerrepeat")]
                public string TimerRepeat { get; private set; }

                [JsonProperty("spotifykey")]
                public string SpotifyKey { get; private set; }

                [JsonProperty("spotifysecret")]
                public string SpotifySecret { get; private set; }

                [JsonProperty("exceptionchannel")]
                public string ExceptionChannel { get; private set; }

                [JsonProperty("cooldown")]	
                public string Cooldown { get; private set; }

                [JsonProperty("nummessages")]
                public string NumMessages { get; private set; }

                [JsonProperty("inbetweentime")]
                public string InBetweenTime { get; private set; }
            }

            public static async Task<ConfigJson> GetJSONDataAsync()
            {
                // first, let's load our configuration file
                await GlobalVars.Log(new LogMessage(LogSeverity.Info, "JsonCfg", "Loading Configuration"));
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                return cfgjson;
            }

            public static ConfigJson GetJSONData()
            {
                // first, let's load our configuration file
                GlobalVars.Log(new LogMessage(LogSeverity.Info, "JsonCfg", "Loading Configuration"));
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = sr.ReadToEnd();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                return cfgjson;
            }
        }

        #endregion

        #region Exception Reporter

        public class ExceptionReporter
        {
            public static async void ReportException(DiscordSocketClient client, Exception e)
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    var builder = new EmbedBuilder();
                    builder.AddInlineField("Exception:", e.Message + "\nSource:\n" + e.Source + "\nStack Trace:\n" + e.StackTrace);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception)
                {
                    try
                    {
                        ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                        ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                        await channel.SendMessageAsync("Exception: " + e.Message + "\n\nSource:\n" + e.Source + "\n\nStack Trace:\n" + e.StackTrace);
                    }
                    catch (Exception)
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", "Unable to connect to the server/channel to report error. Look in the log.txt in the FMBot folder to see it."));
                    }
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", e.Message, e), true);
            }

            public static async void ReportStringAsException(DiscordSocketClient client, string e)
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    var builder = new EmbedBuilder();
                    builder.AddInlineField("Exception:", e);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception)
                {
                    try
                    {
                        ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                        ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                        await channel.SendMessageAsync("Exception: " + e);
                    }
                    catch (Exception)
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", "Unable to connect to the server/channel to report error. Look in the log.txt in the FMBot folder to see it."));
                    }
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", e), true);
            }
        }

        #endregion

        #region Global Variables

        public class GlobalVars
        {
            public static string ConfigFileName = "config.json";
            public static string BasePath = AppDomain.CurrentDomain.BaseDirectory;
            public static string CacheFolder = BasePath + "cache/";
            public static string ServersFolder = BasePath + "servers/";
            public static string FeaturedUserID = "";
            public static int MessageLength = 2000;
            public static int CommandExecutions = 0;
            public static int CommandExecutions_Servers = 0;
            public static int CommandExecutions_DMs = 0;
            private static bool IsUserInDM = false;

            public static TimeSpan SystemUpTime()
            {
                var mo = new ManagementObject(@"\\.\root\cimv2:Win32_OperatingSystem=@");
                var lastBootUp = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
                return DateTime.Now.ToUniversalTime() - lastBootUp.ToUniversalTime();
            }

            public static Task Log(LogMessage arg, bool nowrite = false)
            {
                if (nowrite == false)
                {
                    Console.WriteLine(arg);
                }

                var logger = NLog.LogManager.GetCurrentClassLogger();

                logger.Info(arg);

                return Task.CompletedTask;
            }

            public static string GetLine(string filePath, int line)
            {
                using (var sr = new StreamReader(filePath))
                {
                    for (int i = 1; i < line; i++)
                        sr.ReadLine();
                    return sr.ReadLine();
                }
            }

            public static string MultiLine(params string[] args)
            {
                return string.Join(Environment.NewLine, args);
            }

            public static Bitmap Combine(List<Bitmap> images, bool vertical = false)
            {
                //read all images into memory
                Bitmap finalImage = null;

                try
                {
                    int width = 0;
                    int height = 0;

                    foreach (Bitmap image in images.ToArray())
                    {
                        //create a Bitmap from the file and add it to the list
                        Bitmap bitmap = image;

                        //update the size of the final bitmap
                        if (vertical == true)
                        {
                            width = bitmap.Width > width ? bitmap.Width : width;
                            height += bitmap.Height;
                        }
                        else
                        {
                            width += bitmap.Width;
                            height = bitmap.Height > height ? bitmap.Height : height;
                        }

                        images.Add(bitmap);
                    }

                    //create a bitmap to hold the combined image
                    finalImage = new Bitmap(width, height);

                    //get a graphics object from the image so we can draw on it
                    using (Graphics g = Graphics.FromImage(finalImage))
                    {
                        //set background color
                        g.Clear(System.Drawing.Color.Black);

                        //go through each image and draw it on the final image
                        int offset = 0;
                        foreach (Bitmap image in images)
                        {
                            if (vertical == true)
                            {
                                g.DrawImage(image, new Rectangle(0, offset, image.Width, image.Height));
                                offset += image.Height;
                            }
                            else
                            {
                                g.DrawImage(image, new Rectangle(offset, 0, image.Width, image.Height));
                                offset += image.Width;
                            }
                        }
                    }

                    return finalImage;
                }
                catch (Exception ex)
                {
                    if (finalImage != null)
                        finalImage.Dispose();

                    throw ex;
                }
                finally
                {
                    //clean up memory
                    foreach (Bitmap image in images)
                    {
                        image.Dispose();
                    }
                }
            }

            public static List<List<Bitmap>> splitBitmapList(List<Bitmap> locations, int nSize)
            {
                var list = new List<List<Bitmap>>();

                for (int i = 0; i < locations.Count; i += nSize)
                {
                    list.Add(locations.GetRange(i, Math.Min(nSize, locations.Count - i)));
                }

                return list;
            }

            public static void SetUserTitleEmbed(EmbedBuilder builder, IUser DiscordUser, string LastFMName, ISelfUser SelfUser)
            {
                if (!string.IsNullOrWhiteSpace(FeaturedUserID))
                {
                    if (DiscordUser.Id == Convert.ToUInt64(FeaturedUserID))
                    {
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Owner, Featured User");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Super Admin, Featured User");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Admin, Featured User");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName + ", Featured User");
                        }
                    }
                    else
                    {
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }
                    }
                }
                else
                {
                    if (FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Owner");
                    }
                    else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Super Admin");
                    }
                    else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", " + SelfUser.Username + " Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }
                }
            }

            public static async void CheckIfDMBool(ICommandContext Context)
            {
                IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync();

                if (dm == null)
                    IsUserInDM = false;

                if (Context.Channel.Name == dm.Name)
                {
                    IsUserInDM = true;
                }
                else
                {
                    IsUserInDM = false;
                }
            }

            public static bool GetDMBool()
            {
                return IsUserInDM;
            }

            public static IUser CheckIfDM(IUser user, ICommandContext Context)
            {
                CheckIfDMBool(Context);

                if (IsUserInDM)
                {
                    return user ?? Context.Message.Author;
                }
                else
                {
                    return (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                }
            }

            public static string GetNameString(IUser DiscordUser, ICommandContext Context)
            {
                if (IsUserInDM)
                {
                    return DiscordUser.Username;
                }
                else
                {
                    IGuildUser GuildUser = (IGuildUser)DiscordUser;

                    if (string.IsNullOrWhiteSpace(GuildUser.Nickname))
                    {
                        return GuildUser.Username;
                    }
                    else
                    {
                        return GuildUser.Nickname;
                    }
                }
            }

            public static string SetUserTitleText(IUser DiscordUser, ISelfUser SelfUser)
            {
                string outputString = "";

                if (!string.IsNullOrWhiteSpace(FeaturedUserID))
                {
                    if (DiscordUser.Id == Convert.ToUInt64(FeaturedUserID))
                    {
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Owner, Featured User\n";
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Super Admin, Featured User\n";
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Admin, Featured User\n";
                        }
                        else
                        {
                            outputString = "Featured User\n";
                        }
                    }
                    else
                    {
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Owner\n";
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Super Admin\n";
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            outputString = SelfUser.Username + " Admin\n";
                        }
                        else
                        {
                            outputString = "";
                        }
                    }
                }
                else
                {
                    if (FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        outputString = SelfUser.Username + " Owner\n";
                    }
                    else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                    {
                        outputString = SelfUser.Username + " Super Admin\n";
                    }
                    else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                    {
                        outputString = SelfUser.Username + " Admin\n";
                    }
                    else
                    {
                        outputString = "";
                    }
                }

                return outputString;
            }

            public static void ClearReadOnly(DirectoryInfo parentDirectory)
            {
                if (parentDirectory != null)
                {
                    parentDirectory.Attributes = FileAttributes.Normal;
                    foreach (FileInfo fi in parentDirectory.GetFiles())
                    {
                        fi.Attributes = FileAttributes.Normal;
                    }
                    foreach (DirectoryInfo di in parentDirectory.GetDirectories())
                    {
                        ClearReadOnly(di);
                    }
                }
            }
        }

        #endregion

        #region FMBot Staff Functions

        public class FMBotAdminUtil
        {
            public static bool IsAdmin(IUser user)
            {
                if (DBase.CheckAdmin(user.Id.ToString()))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool IsSuperAdmin(IUser user)
            {
                if (DBase.CheckSuperAdmin(user.Id.ToString()))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool IsOwner(IUser user)
            {
                var cfgjson = JsonCfg.GetJSONData();

                if (IsSoleOwner(user) || DBase.CheckOwner(user.Id.ToString()))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool IsSoleOwner(IUser user)
            {
                var cfgjson = JsonCfg.GetJSONData();

                if (user.Id.Equals(Convert.ToUInt64(cfgjson.BotOwner)))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool HasCommandAccess(IUser user, int mode)
            {
                if (mode == 1)
                {
                    if (IsAdmin(user))
                    {
                        return true;
                    }
                    else if (IsSuperAdmin(user))
                    {
                        return true;
                    }
                    else if (IsOwner(user))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (mode == 2)
                {
                    if (IsSuperAdmin(user))
                    {
                        return true;
                    }
                    else if (IsOwner(user))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (mode == 3)
                {
                    if (IsOwner(user))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            public static bool IsRankAbove(IUser user, IUser user2)
            {
                if (IsAdmin(user) && IsSuperAdmin(user2))
                {
                    return true;
                }
                else if (IsAdmin(user) && IsOwner(user2))
                {
                    return true;
                }
                else if (IsSuperAdmin(user) && IsOwner(user2))
                {
                    return true;
                }
                else if (IsAdmin(user) && IsAdmin(user2))
                {
                    return true;
                }
                else if (IsSuperAdmin(user) && IsSuperAdmin(user2))
                {
                    return true;
                }
                else if (IsOwner(user) && IsOwner(user2))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        #endregion

        #region FMBot Chart Classification

        public class FMBotChart
        {
            public string time;
            public LastfmClient client;
            public string LastFMName;
            public int max;
            public int rows;
            public List<Bitmap> images;
            public IUser DiscordUser;
            public DiscordSocketClient disclient;
            public int mode;
            public bool titles;

            public async Task ChartGenerate()
            {
                try
                {
                    LastStatsTimeSpan timespan = LastStatsTimeSpan.Week;

                    if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                    {
                        timespan = LastStatsTimeSpan.Week;
                    }
                    else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                    {
                        timespan = LastStatsTimeSpan.Month;
                    }
                    else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                    {
                        timespan = LastStatsTimeSpan.Year;
                    }
                    else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                    {
                        timespan = LastStatsTimeSpan.Overall;
                    }

                    string nulltext = "[undefined]";

                    if (mode == 0)
                    {
                        var tracks = await client.User.GetTopAlbums(LastFMName, timespan, 1, max);

                        for (int al = 0; al < max; ++al)
                        {
                            LastAlbum track = tracks.Content.ElementAt(al);

                            string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;

                            try
                            {
                                var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                WebRequest request = WebRequest.Create(ThumbnailImage);
                                WebResponse response = request.GetResponse();
                                Stream responseStream = response.GetResponseStream();
                                Bitmap cover = new Bitmap(responseStream);
                                if (titles)
                                {
                                    Graphics text = Graphics.FromImage(cover);
                                    text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                                    text.DrawColorString(cover, AlbumName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 12.0f));
                                }

                                images.Add(cover);
                            }
                            catch (Exception e)
                            {
                                ExceptionReporter.ReportException(disclient, e);

                                Bitmap cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                                if (titles)
                                {
                                    Graphics text = Graphics.FromImage(cover);
                                    text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                                    text.DrawColorString(cover, AlbumName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 12.0f));
                                }

                                images.Add(cover);
                            }
                        }
                    }
                    else if (mode == 1)
                    {
                        var artists = await client.User.GetTopArtists(LastFMName, timespan, 1, max);
                        for (int al = 0; al < max; ++al)
                        {
                            LastArtist artist = artists.Content.ElementAt(al);

                            string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                            try
                            {
                                var ArtistInfo = await client.Artist.GetInfoAsync(ArtistName);
                                var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                WebRequest request = WebRequest.Create(ThumbnailImage);
                                WebResponse response = request.GetResponse();
                                Stream responseStream = response.GetResponseStream();
                                Bitmap cover = new Bitmap(responseStream);
                                if (titles)
                                {
                                    Graphics text = Graphics.FromImage(cover);
                                    text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                                }

                                images.Add(cover);
                            }
                            catch (Exception e)
                            {
                                ExceptionReporter.ReportException(disclient, e);

                                Bitmap cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                                if (titles)
                                {
                                    Graphics text = Graphics.FromImage(cover);
                                    text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                                }

                                images.Add(cover);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportException(disclient, e);
                }
                finally
                {
                    List<List<Bitmap>> ImageLists = GlobalVars.splitBitmapList(images, rows);

                    List<Bitmap> BitmapList = new List<Bitmap>();

                    foreach (List<Bitmap> list in ImageLists.ToArray())
                    {
                        //combine them into one image
                        Bitmap stitchedRow = GlobalVars.Combine(list);
                        BitmapList.Add(stitchedRow);
                    }

                    Bitmap stitchedImage = GlobalVars.Combine(BitmapList, true);

                    foreach(Bitmap image in BitmapList.ToArray())
                    {
                        image.Dispose();
                    }

                    using (MemoryStream memory = new MemoryStream())
                    {
                        using (FileStream fs = new FileStream(GlobalVars.CacheFolder + DiscordUser.Id + "-chart.png", FileMode.Create, FileAccess.ReadWrite))
                        {
                            stitchedImage.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                            byte[] bytes = memory.ToArray();
                            fs.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
            }
        }

        #endregion

        #region User Classification

        public class User
        {
            public ulong ID { get; set; }
            public DateTime LastRequestAfterCooldownBegins { get; set; }
            public DateTime LastRequestAfterMessageSent { get; set; }
            public static List<User> Users = new List<User>(); // list of all users
            public int messagesSent { get; set; }
            public bool isInCooldown { get; set; }
            public bool isWatchingMessages { get; set; }

            public static bool IncomingRequest(DiscordSocketClient client, ulong DiscordID)//requesting user == username of the person messaging your bot
            {
                try
                {
                    var cfgjson = JsonCfg.GetJSONData();

                    User TempUser = Users.FirstOrDefault(User => User.ID.Equals(DiscordID));
                    if (TempUser != null)// check to see if you have handled a request in the past from this user.
                    {
                        TempUser.messagesSent += 1;

                        if (TempUser.isWatchingMessages == false)
                        {
                            Users.Find(User => User.ID.Equals(DiscordID)).LastRequestAfterMessageSent = DateTime.Now;
                            TempUser.isWatchingMessages = true;
                        }
                        
                        if ((DateTime.Now - TempUser.LastRequestAfterMessageSent).TotalSeconds >= Convert.ToDouble(cfgjson.InBetweenTime))
                        {
                            TempUser.messagesSent = 1;
                            Users.Find(User => User.ID.Equals(DiscordID)).LastRequestAfterMessageSent = DateTime.Now;
                            TempUser.isWatchingMessages = true;
                            return true;
                        }

                        if (TempUser.messagesSent >= Convert.ToInt32(cfgjson.NumMessages))
                        {
                            if (TempUser.isInCooldown == false)
                            {
                                Users.Find(User => User.ID.Equals(DiscordID)).LastRequestAfterCooldownBegins = DateTime.Now;
                                TempUser.isInCooldown = true;
                                TempUser.isWatchingMessages = false;
                            }

                            double curTime = (DateTime.Now - TempUser.LastRequestAfterCooldownBegins).TotalSeconds;

                            if (curTime >= Convert.ToDouble(cfgjson.Cooldown)) // checks if more than 30 seconds have passed between the last requests send by the user
                            {
                                TempUser.messagesSent = 1;
                                TempUser.isInCooldown = false;
                                Users.Find(User => User.ID.Equals(DiscordID)).LastRequestAfterMessageSent = DateTime.Now;
                                TempUser.isWatchingMessages = true;
                                return true;
                            }
                            else // if less than 30 seconds has passed return false.
                            {
                                var user = client.GetUser(DiscordID);
                                int curTimeEstimate = Convert.ToInt32(cfgjson.Cooldown) - (int)curTime;
                                if (curTimeEstimate > 1)
                                {
                                    user.SendMessageAsync("Sorry, but you have been put under a " + cfgjson.Cooldown + " second coolddown. You have to wait " + curTimeEstimate + " seconds for this to expire.");
                                }
                                else
                                {
                                    user.SendMessageAsync("Sorry, but you have been put under a " + cfgjson.Cooldown + " second coolddown. You have to wait " + curTimeEstimate + " second for this to expire.");
                                }
                                return false;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else // if no user is found, create a new user, and add it to the list
                    {
                        User NewUser = new User();
                        NewUser.ID = DiscordID;
                        NewUser.messagesSent = 1;
                        NewUser.isInCooldown = false;
                        NewUser.LastRequestAfterMessageSent = DateTime.Now;
                        NewUser.isWatchingMessages = true;
                        Users.Add(NewUser);
                        return true;
                    }
                }
                catch(Exception e)
                {
                    ExceptionReporter.ReportException(client, e);
                    return true;
                }
            }
        }
        #endregion

        #region Fast File Enumerator
        /// <summary>
        /// Contains information about a file returned by the 
        /// <see cref="FastDirectoryEnumerator"/> class.
        /// </summary>
        [Serializable]
        public class FileData
        {
            /// <summary>
            /// Attributes of the file.
            /// </summary>
            public readonly FileAttributes Attributes;

            public DateTime CreationTime
            {
                get { return this.CreationTimeUtc.ToLocalTime(); }
            }

            /// <summary>
            /// File creation time in UTC
            /// </summary>
            public readonly DateTime CreationTimeUtc;

            /// <summary>
            /// Gets the last access time in local time.
            /// </summary>
            public DateTime LastAccesTime
            {
                get { return this.LastAccessTimeUtc.ToLocalTime(); }
            }

            /// <summary>
            /// File last access time in UTC
            /// </summary>
            public readonly DateTime LastAccessTimeUtc;

            /// <summary>
            /// Gets the last access time in local time.
            /// </summary>
            public DateTime LastWriteTime
            {
                get { return this.LastWriteTimeUtc.ToLocalTime(); }
            }

            /// <summary>
            /// File last write time in UTC
            /// </summary>
            public readonly DateTime LastWriteTimeUtc;

            /// <summary>
            /// Size of the file in bytes
            /// </summary>
            public readonly long Size;

            /// <summary>
            /// Name of the file
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Full path to the file.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            public override string ToString()
            {
                return this.Name;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="FileData"/> class.
            /// </summary>
            /// <param name="dir">The directory that the file is stored at</param>
            /// <param name="findData">WIN32_FIND_DATA structure that this
            /// object wraps.</param>
            internal FileData(string dir, WIN32_FIND_DATA findData)
            {
                this.Attributes = findData.dwFileAttributes;


                this.CreationTimeUtc = ConvertDateTime(findData.ftCreationTime_dwHighDateTime,
                                                    findData.ftCreationTime_dwLowDateTime);

                this.LastAccessTimeUtc = ConvertDateTime(findData.ftLastAccessTime_dwHighDateTime,
                                                    findData.ftLastAccessTime_dwLowDateTime);

                this.LastWriteTimeUtc = ConvertDateTime(findData.ftLastWriteTime_dwHighDateTime,
                                                    findData.ftLastWriteTime_dwLowDateTime);

                this.Size = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);

                this.Name = findData.cFileName;
                this.Path = System.IO.Path.Combine(dir, findData.cFileName);
            }

            private static long CombineHighLowInts(uint high, uint low)
            {
                return (((long)high) << 0x20) | low;
            }

            private static DateTime ConvertDateTime(uint high, uint low)
            {
                long fileTime = CombineHighLowInts(high, low);
                return DateTime.FromFileTimeUtc(fileTime);
            }
        }

        /// <summary>
        /// Contains information about the file that is found 
        /// by the FindFirstFile or FindNextFile functions.
        /// </summary>
        [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto), BestFitMapping(false)]
        internal class WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public uint ftCreationTime_dwLowDateTime;
            public uint ftCreationTime_dwHighDateTime;
            public uint ftLastAccessTime_dwLowDateTime;
            public uint ftLastAccessTime_dwHighDateTime;
            public uint ftLastWriteTime_dwLowDateTime;
            public uint ftLastWriteTime_dwHighDateTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;

            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            public override string ToString()
            {
                return "File name=" + cFileName;
            }
        }

        /// <summary>
        /// A fast enumerator of files in a directory.  Use this if you need to get attributes for 
        /// all files in a directory.
        /// </summary>
        /// <remarks>
        /// This enumerator is substantially faster than using <see cref="Directory.GetFiles(string)"/>
        /// and then creating a new FileInfo object for each path.  Use this version when you 
        /// will need to look at the attibutes of each file returned (for example, you need
        /// to check each file in a directory to see if it was modified after a specific date).
        /// </remarks>
        public static class FastDirectoryEnumerator
        {
            /// <summary>
            /// Gets <see cref="FileData"/> for all the files in a directory.
            /// </summary>
            /// <param name="path">The path to search.</param>
            /// <returns>An object that implements <see cref="IEnumerable{FileData}"/> and 
            /// allows you to enumerate the files in the given directory.</returns>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="path"/> is a null reference (Nothing in VB)
            /// </exception>
            public static IEnumerable<FileData> EnumerateFiles(string path)
            {
                return FastDirectoryEnumerator.EnumerateFiles(path, "*");
            }

            /// <summary>
            /// Gets <see cref="FileData"/> for all the files in a directory that match a 
            /// specific filter.
            /// </summary>
            /// <param name="path">The path to search.</param>
            /// <param name="searchPattern">The search string to match against files in the path.</param>
            /// <returns>An object that implements <see cref="IEnumerable{FileData}"/> and 
            /// allows you to enumerate the files in the given directory.</returns>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="path"/> is a null reference (Nothing in VB)
            /// </exception>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="filter"/> is a null reference (Nothing in VB)
            /// </exception>
            public static IEnumerable<FileData> EnumerateFiles(string path, string searchPattern)
            {
                return FastDirectoryEnumerator.EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
            }

            /// <summary>
            /// Gets <see cref="FileData"/> for all the files in a directory that 
            /// match a specific filter, optionally including all sub directories.
            /// </summary>
            /// <param name="path">The path to search.</param>
            /// <param name="searchPattern">The search string to match against files in the path.</param>
            /// <param name="searchOption">
            /// One of the SearchOption values that specifies whether the search 
            /// operation should include all subdirectories or only the current directory.
            /// </param>
            /// <returns>An object that implements <see cref="IEnumerable{FileData}"/> and 
            /// allows you to enumerate the files in the given directory.</returns>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="path"/> is a null reference (Nothing in VB)
            /// </exception>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="filter"/> is a null reference (Nothing in VB)
            /// </exception>
            /// <exception cref="ArgumentOutOfRangeException">
            /// <paramref name="searchOption"/> is not one of the valid values of the
            /// <see cref="System.IO.SearchOption"/> enumeration.
            /// </exception>
            public static IEnumerable<FileData> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
            {
                if (path == null)
                {
                    throw new ArgumentNullException("path");
                }
                if (searchPattern == null)
                {
                    throw new ArgumentNullException("searchPattern");
                }
                if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                {
                    throw new ArgumentOutOfRangeException("searchOption");
                }

                string fullPath = Path.GetFullPath(path);

                return new FileEnumerable(fullPath, searchPattern, searchOption);
            }

            /// <summary>
            /// Gets <see cref="FileData"/> for all the files in a directory that match a 
            /// specific filter.
            /// </summary>
            /// <param name="path">The path to search.</param>
            /// <param name="searchPattern">The search string to match against files in the path.</param>
            /// <returns>An object that implements <see cref="IEnumerable{FileData}"/> and 
            /// allows you to enumerate the files in the given directory.</returns>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="path"/> is a null reference (Nothing in VB)
            /// </exception>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="filter"/> is a null reference (Nothing in VB)
            /// </exception>
            public static FileData[] GetFiles(string path, string searchPattern, SearchOption searchOption)
            {
                IEnumerable<FileData> e = FastDirectoryEnumerator.EnumerateFiles(path, searchPattern, searchOption);
                List<FileData> list = new List<FileData>(e);

                FileData[] retval = new FileData[list.Count];
                list.CopyTo(retval);

                return retval;
            }

            /// <summary>
            /// Provides the implementation of the 
            /// <see cref="T:System.Collections.Generic.IEnumerable`1"/> interface
            /// </summary>
            private class FileEnumerable : IEnumerable<FileData>
            {
                private readonly string m_path;
                private readonly string m_filter;
                private readonly SearchOption m_searchOption;

                /// <summary>
                /// Initializes a new instance of the <see cref="FileEnumerable"/> class.
                /// </summary>
                /// <param name="path">The path to search.</param>
                /// <param name="filter">The search string to match against files in the path.</param>
                /// <param name="searchOption">
                /// One of the SearchOption values that specifies whether the search 
                /// operation should include all subdirectories or only the current directory.
                /// </param>
                public FileEnumerable(string path, string filter, SearchOption searchOption)
                {
                    m_path = path;
                    m_filter = filter;
                    m_searchOption = searchOption;
                }

                #region IEnumerable<FileData> Members

                /// <summary>
                /// Returns an enumerator that iterates through the collection.
                /// </summary>
                /// <returns>
                /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can 
                /// be used to iterate through the collection.
                /// </returns>
                public IEnumerator<FileData> GetEnumerator()
                {
                    return new FileEnumerator(m_path, m_filter, m_searchOption);
                }

                #endregion

                #region IEnumerable Members

                /// <summary>
                /// Returns an enumerator that iterates through a collection.
                /// </summary>
                /// <returns>
                /// An <see cref="T:System.Collections.IEnumerator"/> object that can be 
                /// used to iterate through the collection.
                /// </returns>
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    return new FileEnumerator(m_path, m_filter, m_searchOption);
                }

                #endregion
            }

            /// <summary>
            /// Wraps a FindFirstFile handle.
            /// </summary>
            private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
                [DllImport("kernel32.dll")]
                private static extern bool FindClose(IntPtr handle);

                /// <summary>
                /// Initializes a new instance of the <see cref="SafeFindHandle"/> class.
                /// </summary>
                [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
                internal SafeFindHandle()
                    : base(true)
                {
                }

                /// <summary>
                /// When overridden in a derived class, executes the code required to free the handle.
                /// </summary>
                /// <returns>
                /// true if the handle is released successfully; otherwise, in the 
                /// event of a catastrophic failure, false. In this case, it 
                /// generates a releaseHandleFailed MDA Managed Debugging Assistant.
                /// </returns>
                protected override bool ReleaseHandle()
                {
                    return FindClose(base.handle);
                }
            }

            /// <summary>
            /// Provides the implementation of the 
            /// <see cref="T:System.Collections.Generic.IEnumerator`1"/> interface
            /// </summary>
            [System.Security.SuppressUnmanagedCodeSecurity]
            private class FileEnumerator : IEnumerator<FileData>
            {
                [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
                private static extern SafeFindHandle FindFirstFile(string fileName,
                    [In, Out] WIN32_FIND_DATA data);

                [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
                private static extern bool FindNextFile(SafeFindHandle hndFindFile,
                        [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

                /// <summary>
                /// Hold context information about where we current are in the directory search.
                /// </summary>
                private class SearchContext
                {
                    public readonly string Path;
                    public Stack<string> SubdirectoriesToProcess;

                    public SearchContext(string path)
                    {
                        this.Path = path;
                    }
                }

                private string m_path;
                private string m_filter;
                private SearchOption m_searchOption;
                private Stack<SearchContext> m_contextStack;
                private SearchContext m_currentContext;

                private SafeFindHandle m_hndFindFile;
                private WIN32_FIND_DATA m_win_find_data = new WIN32_FIND_DATA();

                /// <summary>
                /// Initializes a new instance of the <see cref="FileEnumerator"/> class.
                /// </summary>
                /// <param name="path">The path to search.</param>
                /// <param name="filter">The search string to match against files in the path.</param>
                /// <param name="searchOption">
                /// One of the SearchOption values that specifies whether the search 
                /// operation should include all subdirectories or only the current directory.
                /// </param>
                public FileEnumerator(string path, string filter, SearchOption searchOption)
                {
                    m_path = path;
                    m_filter = filter;
                    m_searchOption = searchOption;
                    m_currentContext = new SearchContext(path);

                    if (m_searchOption == SearchOption.AllDirectories)
                    {
                        m_contextStack = new Stack<SearchContext>();
                    }
                }

                #region IEnumerator<FileData> Members

                /// <summary>
                /// Gets the element in the collection at the current position of the enumerator.
                /// </summary>
                /// <value></value>
                /// <returns>
                /// The element in the collection at the current position of the enumerator.
                /// </returns>
                public FileData Current
                {
                    get { return new FileData(m_path, m_win_find_data); }
                }

                #endregion

                #region IDisposable Members

                /// <summary>
                /// Performs application-defined tasks associated with freeing, releasing, 
                /// or resetting unmanaged resources.
                /// </summary>
                public void Dispose()
                {
                    if (m_hndFindFile != null)
                    {
                        m_hndFindFile.Dispose();
                    }
                }

                #endregion

                #region IEnumerator Members

                /// <summary>
                /// Gets the element in the collection at the current position of the enumerator.
                /// </summary>
                /// <value></value>
                /// <returns>
                /// The element in the collection at the current position of the enumerator.
                /// </returns>
                object System.Collections.IEnumerator.Current
                {
                    get { return new FileData(m_path, m_win_find_data); }
                }

                /// <summary>
                /// Advances the enumerator to the next element of the collection.
                /// </summary>
                /// <returns>
                /// true if the enumerator was successfully advanced to the next element; 
                /// false if the enumerator has passed the end of the collection.
                /// </returns>
                /// <exception cref="T:System.InvalidOperationException">
                /// The collection was modified after the enumerator was created.
                /// </exception>
                public bool MoveNext()
                {
                    bool retval = false;

                    //If the handle is null, this is first call to MoveNext in the current 
                    // directory.  In that case, start a new search.
                    if (m_currentContext.SubdirectoriesToProcess == null)
                    {
                        if (m_hndFindFile == null)
                        {
                            new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_path).Demand();

                            string searchPath = Path.Combine(m_path, m_filter);
                            m_hndFindFile = FindFirstFile(searchPath, m_win_find_data);
                            retval = !m_hndFindFile.IsInvalid;
                        }
                        else
                        {
                            //Otherwise, find the next item.
                            retval = FindNextFile(m_hndFindFile, m_win_find_data);
                        }
                    }

                    //If the call to FindNextFile or FindFirstFile succeeded...
                    if (retval)
                    {
                        if (((FileAttributes)m_win_find_data.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            //Ignore folders for now.   We call MoveNext recursively here to 
                            // move to the next item that FindNextFile will return.
                            return MoveNext();
                        }
                    }
                    else if (m_searchOption == SearchOption.AllDirectories)
                    {
                        //SearchContext context = new SearchContext(m_hndFindFile, m_path);
                        //m_contextStack.Push(context);
                        //m_path = Path.Combine(m_path, m_win_find_data.cFileName);
                        //m_hndFindFile = null;

                        if (m_currentContext.SubdirectoriesToProcess == null)
                        {
                            string[] subDirectories = Directory.GetDirectories(m_path);
                            m_currentContext.SubdirectoriesToProcess = new Stack<string>(subDirectories);
                        }

                        if (m_currentContext.SubdirectoriesToProcess.Count > 0)
                        {
                            string subDir = m_currentContext.SubdirectoriesToProcess.Pop();

                            m_contextStack.Push(m_currentContext);
                            m_path = subDir;
                            m_hndFindFile = null;
                            m_currentContext = new SearchContext(m_path);
                            return MoveNext();
                        }

                        //If there are no more files in this directory and we are 
                        // in a sub directory, pop back up to the parent directory and
                        // continue the search from there.
                        if (m_contextStack.Count > 0)
                        {
                            m_currentContext = m_contextStack.Pop();
                            m_path = m_currentContext.Path;
                            if (m_hndFindFile != null)
                            {
                                m_hndFindFile.Close();
                                m_hndFindFile = null;
                            }

                            return MoveNext();
                        }
                    }

                    return retval;
                }

                /// <summary>
                /// Sets the enumerator to its initial position, which is before the first element in the collection.
                /// </summary>
                /// <exception cref="T:System.InvalidOperationException">
                /// The collection was modified after the enumerator was created.
                /// </exception>
                public void Reset()
                {
                    m_hndFindFile = null;
                }

                #endregion
            }
        }
        #endregion
    }
}

//extentions below

#region Class Extentions

public static class TimeSpanExtentions
{
    public static string ToReadableAgeString(this TimeSpan span)
    {
        return string.Format("{0:0}", span.Days / 365.25);
    }

    public static string ToReadableString(this TimeSpan span)
    {
        string formatted = string.Format("{0}{1}{2}{3}",
            span.Duration().Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? String.Empty : "s") : string.Empty,
            span.Duration().Hours > 0 ? string.Format("{0:0} hour{1}, ", span.Hours, span.Hours == 1 ? String.Empty : "s") : string.Empty,
            span.Duration().Minutes > 0 ? string.Format("{0:0} minute{1}, ", span.Minutes, span.Minutes == 1 ? String.Empty : "s") : string.Empty,
            span.Duration().Seconds > 0 ? string.Format("{0:0} second{1}", span.Seconds, span.Seconds == 1 ? String.Empty : "s") : string.Empty);

        if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

        if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

        return formatted;
    }
}

public static class StringExtentions
{
    public static IEnumerable<string> SplitByMessageLength(this string str)
    {
        int MessageLength = 2000;

        for (int index = 0; index < str.Length; index += MessageLength)
        {
            yield return str.Substring(index, Math.Min(MessageLength, str.Length - index));
        }
    }
}

public static class BitmapExtentions
{
    public static byte MostDifferent (byte original) 
    {
        if(original < 0x80) 
        {
            return 0xff;
        } 
        else 
        {
            return 0x00;
        }
    }
    
    public static System.Drawing.Color MostDifferent (System.Drawing.Color original) 
    {
        byte r = MostDifferent(original.R);
        byte g = MostDifferent(original.G);
        byte b = MostDifferent(original.B);
        return System.Drawing.Color.FromArgb(r,g,b);
    }
    
    public static unsafe System.Drawing.Color AverageColor (Bitmap bmp, Rectangle r) 
    {
        BitmapData bmd = bmp.LockBits (r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int s = bmd.Stride;
        int cr = 0;
        int cg = 0;
        int cb = 0;
        int* clr = (int*)(void*)bmd.Scan0;
        int tmp;
        int* row = clr;
        for (int i = 0; i < r.Height; i++) 
        {
            int* col = row;
            for (int j = 0; j < r.Width; j++) 
            {
                tmp = *col;
                cr += (tmp >> 0x10) & 0xff;
                cg += (tmp >> 0x08) & 0xff;
                cb += tmp & 0xff;
                col++;
            }
            row += s>>0x02;
        }
        int div = r.Width * r.Height;
        int d2 = div >> 0x01;
        cr = (cr + d2) / div;
        cg = (cg + d2) / div;
        cb = (cb + d2) / div;
        bmp.UnlockBits (bmd);
        return System.Drawing.Color.FromArgb (cr, cg, cb);
    }
    
    public static void DrawColorString (this Graphics g, Bitmap bmp, string text, Font font, PointF point) 
    {
        SizeF sf = g.MeasureString (text, font);
        Rectangle r = new Rectangle (Point.Truncate (point), Size.Ceiling (sf));
        r.Intersect (new Rectangle(0,0,bmp.Width,bmp.Height));
        System.Drawing.Color brsh = MostDifferent (AverageColor (bmp, r));
        g.DrawString (text, font, new SolidBrush (brsh), point);
    }
}

#endregion
