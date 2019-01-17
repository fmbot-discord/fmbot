using Discord;
using Discord.Commands;
using Discord.WebSocket;
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


            public async static Task<IGuildUser> ConvertIDToGuildUser(IGuild guild, ulong id)
            {
                IReadOnlyCollection<IGuildUser> users = await guild.GetUsersAsync();

                foreach (IGuildUser user in users)
                {
                    if (user.Id == id)
                    {
                        return user;
                    }
                }

                return null;
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

                List<string> list = new List<string>(friends);

                foreach (string friend in friendlist)
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
                string[] friendsLower = friends.Select(s => s.ToLowerInvariant()).ToArray();

                int listcount = friendlist.Count();

                List<string> list = new List<string>(friends);


                foreach (string friend in friendlist)
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
                string json = "";
                using (FileStream fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (StreamReader sr = new StreamReader(fs, new UTF8Encoding(false)))
                {
                    json = await sr.ReadToEndAsync();
                }

                // next, let's load the values from that file
                // to our client's configuration
                ConfigJson cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                return cfgjson;
            }

            public static ConfigJson GetJSONData()
            {
                // first, let's load our configuration file
                GlobalVars.Log(new LogMessage(LogSeverity.Info, "JsonCfg", "Loading Configuration"));
                string json = "";
                using (FileStream fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (StreamReader sr = new StreamReader(fs, new UTF8Encoding(false)))
                {
                    json = sr.ReadToEnd();
                }

                // next, let's load the values from that file
                // to our client's configuration
                ConfigJson cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                return cfgjson;
            }
        }

        #endregion

        #region Exception Reporter

        public class ExceptionReporter
        {
            public static async void ReportException(DiscordSocketClient client = null, Exception e = null)
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e.Message + "\nSource:\n" + e.Source + "\nStack Trace:\n" + e.StackTrace);

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

            public static async void ReportShardedException(DiscordShardedClient client = null, Exception e = null)
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e.Message + "\nSource:\n" + e.Source + "\nStack Trace:\n" + e.StackTrace);

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

            public static async void ReportStringAsException(DiscordShardedClient client, string e)
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e);

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
            public static string FeaturedUserID = "";
            public static int MessageLength = 2000;
            public static int CommandExecutions = 0;
            public static int CommandExecutions_Servers = 0;
            public static int CommandExecutions_DMs = 0;
            private static bool IsUserInDM = false;

            public static TimeSpan SystemUpTime()
            {
                ManagementObject mo = new ManagementObject(@"\\.\root\cimv2:Win32_OperatingSystem=@");
                DateTime lastBootUp = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
                return DateTime.Now.ToUniversalTime() - lastBootUp.ToUniversalTime();
            }

            public static Task Log(LogMessage arg, bool nowrite = false)
            {
                if (nowrite == false)
                {
                    Console.WriteLine(arg);
                }

                NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

                logger.Info(arg);

                return Task.CompletedTask;
            }

            public static string GetLine(string filePath, int line)
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    for (int i = 1; i < line; i++)
                    {
                        sr.ReadLine();
                    }

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
                    {
                        finalImage.Dispose();
                    }

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
                List<List<Bitmap>> list = new List<List<Bitmap>>();

                for (int i = 0; i < locations.Count; i += nSize)
                {
                    list.Add(locations.GetRange(i, Math.Min(nSize, locations.Count - i)));
                }

                return list;
            }


            public static async void CheckIfDMBool(ICommandContext Context)
            {
                IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync();

                if (dm == null)
                {
                    IsUserInDM = false;
                }

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



        #region User Classification

        public class CooldownUser
        {
            public ulong ID { get; set; }
            public DateTime LastRequestAfterCooldownBegins { get; set; }
            public DateTime LastRequestAfterMessageSent { get; set; }
            public static List<CooldownUser> Users = new List<CooldownUser>(); // list of all users
            public int messagesSent { get; set; }
            public bool isInCooldown { get; set; }
            public bool isWatchingMessages { get; set; }

            public static bool IncomingRequest(DiscordShardedClient client, ulong DiscordID)//requesting user == username of the person messaging your bot
            {
                try
                {
                    JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

                    CooldownUser TempUser = Users.FirstOrDefault(User => User.ID.Equals(DiscordID));
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
                                SocketUser user = client.GetUser(DiscordID);
                                int curTimeEstimate = Convert.ToInt32(cfgjson.Cooldown) - (int)curTime;
                                if (curTimeEstimate > 9)
                                {
                                    user.SendMessageAsync("Sorry, but you have been put under a " + cfgjson.Cooldown + " second cooldown. You have to wait " + curTimeEstimate + " seconds for this to expire.");
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
                        CooldownUser NewUser = new CooldownUser();
                        NewUser.ID = DiscordID;
                        NewUser.messagesSent = 1;
                        NewUser.isInCooldown = false;
                        NewUser.LastRequestAfterMessageSent = DateTime.Now;
                        NewUser.isWatchingMessages = true;
                        Users.Add(NewUser);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    //ExceptionReporter.ReportException(client, e);
                    return true;
                }
            }
        }
        #endregion

     }
}