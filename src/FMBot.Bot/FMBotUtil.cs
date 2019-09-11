using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;

namespace FMBot.Bot
{
    public static class FMBotUtil
    {
        #region Exception Reporter

        public class ExceptionReporter
        {
            private readonly ILogger _logger;

            public ExceptionReporter(ILogger logger)
            {
                _logger = logger;
            }

            public static async void ReportException(DiscordSocketClient client = null, Exception e = null)
            {
                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e.Message + "\nSource:\n" + e.Source + "\nStack Trace:\n" + e.StackTrace);

                    await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    try
                    {
                        ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                        ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                        await channel.SendMessageAsync("Exception: " + e.Message + "\n\nSource:\n" + e.Source + "\n\nStack Trace:\n" + e.StackTrace).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", "Unable to connect to the server/channel to report error. Look in the log.txt in the FMBot folder to see it. \n" + e.Message)).ConfigureAwait(false);
                    }


                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", e.Message, e), true).ConfigureAwait(false);
            }

            public static async void ReportShardedException(DiscordShardedClient client = null, Exception e = null)
            {
                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e.Message + "\nSource:\n" + e.Source + "\nStack Trace:\n" + e.StackTrace);

                    await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    try
                    {
                        ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                        ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                        await channel.SendMessageAsync("Exception: " + e.Message + "\n\nSource:\n" + e.Source + "\n\nStack Trace:\n" + e.StackTrace).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", "Unable to connect to the server/channel to report error. Look in the log.txt in the FMBot folder to see it. \n" + e.Message)).ConfigureAwait(false);
                    }
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", e.Message, e), true).ConfigureAwait(false);
            }

            public static async void ReportStringAsException(DiscordShardedClient client, string e)
            {
                try
                {
                    ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.AddField("Exception:", e);

                    await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    try
                    {
                        ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                        ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.ExceptionChannel);

                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                        await channel.SendMessageAsync("Exception: " + e).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", "Unable to connect to the server/channel to report error. Look in the log.txt in the FMBot folder to see it.")).ConfigureAwait(false);
                    }
                }

                await GlobalVars.Log(new LogMessage(LogSeverity.Warning, "ExceptionReporter", e), true).ConfigureAwait(false);
            }
        }

        #endregion

        #region Global Variables

        public static class GlobalVars
        {
            public static Dictionary<string, string> CensoredAlbums = new Dictionary<string, string>()
            {
                {"Death Grips", "No Love Deep Web"},
                {"ミドリ(Midori)", "あらためまして、はじめまして、ミドリです。(aratamemashite hajimemashite midori desu)"},
                {"Midori", "ratamemashite hajimemashite midori desu"},
                {"ミドリ", "あらためまして、はじめまして、ミドリです"},
                {"Xiu Xiu", "A Promise"},
                {"Carcass","Reek of Putrefaction"},
                {"Cattle Decapitation", "Human Jerky"}
            };
            public static string ConfigFileName = "config.json";
            public static string BasePath = AppDomain.CurrentDomain.BaseDirectory;
            public static string CacheFolder = BasePath + "cache/";
            public static string CoversFolder = BasePath + "covers/";
            public static string FeaturedUserID = "";
            public static int MessageLength = 2000;
            public static int CommandExecutions;
            public static int CommandExecutions_Servers;
            public static int CommandExecutions_DMs;
            public static Hashtable charts = new Hashtable();

            private static bool IsUserInDM;

            public static string GetChartFileName(ulong id)
            {
                return CacheFolder + id.ToString() + "-chart.png";
            }

            public static async Task<MemoryStream> GetChartStreamAsync(ulong id)
            {
                MemoryStream dest = new MemoryStream();
                string fileName = GetChartFileName(id);
                Bitmap chartBitmap = (Bitmap)charts[fileName];
                chartBitmap.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
                dest.Position = 0;

                return dest;
            }

            public static TimeSpan SystemUpTime()
            {
                var ticks = Stopwatch.GetTimestamp();
                var uptime = ((double)ticks) / Stopwatch.Frequency;
                return TimeSpan.FromSeconds(uptime);
            }

            public static Task Log(LogMessage arg, bool nowrite = false)
            {
                if (!nowrite)
                {
                    Console.WriteLine(arg);
                }
                LogManager.ThrowExceptions = true;
                Logger logger = LogManager.GetLogger("logger");

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
                        if (vertical)
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
                            if (vertical)
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
                catch (Exception)
                {
                    if (finalImage != null)
                    {
                        finalImage.Dispose();
                    }

                    throw;
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
                IDMChannel dm = await Context.User.GetOrCreateDMChannelAsync().ConfigureAwait(false);

                if (dm == null)
                {
                    IsUserInDM = false;
                }

                IsUserInDM = Context.Channel.Name == dm.Name;
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
    }
}