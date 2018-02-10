
using Discord;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot_Discord
{
    public class FMBotUtil
    {
        public class DBase
        {
            public static string FMAdminString = "IsAdmin";
            public static string FMSuperAdminString = "IsSuperAdmin";

            public static void WriteEntry(string id, string name, int fmval = 0, int IsAdmin = 0)
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

                File.WriteAllText(GlobalVars.UsersFolder + id + ".txt", name + Environment.NewLine + fmval.ToString() + Environment.NewLine + AdminString);
                File.SetAttributes(GlobalVars.UsersFolder + id + ".txt", FileAttributes.Normal);
            }

            public static void RemoveEntry(string id)
            {
                if (File.Exists(GlobalVars.UsersFolder + id + ".txt"))
                {
                    File.SetAttributes(GlobalVars.UsersFolder + id + ".txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.UsersFolder + id + ".txt");
                }
                if (File.Exists(GlobalVars.UsersFolder + id + "-friends.txt"))
                {
                    File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.UsersFolder + id + "-friends.txt");
                }
                if (File.Exists(GlobalVars.UsersFolder + id + "-chart.jpg"))
                {
                    File.SetAttributes(GlobalVars.UsersFolder + id + "-chart.jpg", FileAttributes.Normal);
                    File.Delete(GlobalVars.UsersFolder + id + "-chart.jpg");
                }
            }

            public static bool EntryExists(string id)
            {
                return File.Exists(GlobalVars.UsersFolder + id + ".txt");
            }

            public static string GetNameForID(string id)
            {
                string line;

                using (StreamReader file = new StreamReader(GlobalVars.UsersFolder + id + ".txt"))
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
                foreach (string file in Directory.GetFiles(GlobalVars.UsersFolder, "*.txt"))
                {
                    if (File.ReadAllText(file).Contains(name))
                    {
                        string nameConvert = Path.GetFileNameWithoutExtension(file);
                        ulong DiscordID = Convert.ToUInt64(nameConvert);
                        return DiscordID;
                    }
                }

                return 0;
            }

            public static string GetRandFMName()
            {
                Random rand = new Random();
                List<string> files = Directory.GetFiles(GlobalVars.UsersFolder).Where(F => F.ToLower().EndsWith(".txt")).ToList();
                string randomFile = files[rand.Next(0, files.Count)];

                string line;

                using (StreamReader file = new StreamReader(randomFile))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        file.Close();
                        return line;
                    }
                }

                return "NULL";
            }

            public static int GetModeIntForID(string id)
            {
                string line;

                using (StreamReader file = new StreamReader(GlobalVars.UsersFolder + id + ".txt"))
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
                else
                {
                    return 4;
                }
            }

            public static string GetNameForModeInt(int mode)
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
                else
                {
                    return "NULL";
                }
            }

            public static bool CheckAdmin(string id)
            {
                if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMAdminString)))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static bool CheckSuperAdmin(string id)
            {
                if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMSuperAdminString)))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static string GetAdminStringFromID(string id)
            {
                if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMAdminString)))
                {
                    return FMAdminString;
                }
                else if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMSuperAdminString)))
                {
                    return FMSuperAdminString;
                }
                else
                {
                    return "";
                }
            }

            public static int GetAdminIntFromID(string id)
            {
                if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMAdminString)))
                {
                    return 1;
                }
                else if (File.ReadLines(GlobalVars.UsersFolder + id + ".txt").Any(line => line.Contains(FMSuperAdminString)))
                {
                    return 2;
                }
                else
                {
                    return 0;
                }
            }

            public static void WriteFriendsEntry(string id, params string[] stringArray)
            {
                string text = "";
                foreach (var friend in stringArray)
                {
                    text += friend;
                    text += Environment.NewLine;
                }
                File.WriteAllText(GlobalVars.UsersFolder + id + "-friends.txt", text);
                File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
            }

            public static void AddFriendsEntry(string id, params string[] stringArray)
            {
                string text = "";
                foreach (var friend in stringArray)
                {
                    text += friend;
                    text += Environment.NewLine;
                }
                File.AppendAllText(GlobalVars.UsersFolder + id + "-friends.txt", text);
                File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
            }

            public static void RemoveFriends(string id)
            {
                if (File.Exists(GlobalVars.UsersFolder + id + "-friends.txt"))
                {
                    File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
                    File.Delete(GlobalVars.UsersFolder + id + "-friends.txt");
                }
            }

            public static string[] GetFriendsForID(string id)
            {
                string[] lines = File.ReadAllLines(GlobalVars.UsersFolder + id + "-friends.txt");
                return lines;
            }
        }

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

                [JsonProperty("vultrkey")]
                public string VultrKey { get; private set; }

                [JsonProperty("vultrsubid")]
                public string VultrSubID { get; private set; }

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
            }

            public static async Task<ConfigJson> GetJSONDataAsync()
            {
                // first, let's load our configuration file
                Console.WriteLine("Loading Configuration");
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
                Console.WriteLine("Loading Configuration");
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

        public class GlobalVars
        {
            public static string ConfigFileName = "config.json";
            public static string BasePath = AppDomain.CurrentDomain.BaseDirectory;
            public static string UsersFolder = BasePath + "users/";

            public static string GetLine(string filePath, int line)
            {
                using (var sr = new StreamReader(filePath))
                {
                    for (int i = 1; i < line; i++)
                        sr.ReadLine();
                    return sr.ReadLine();
                }
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
        }

        public class FMBotAdminUtil
        {
            public static bool IsAdmin(IUser user)
            {
                if (IsSuperAdmin(user) || DBase.CheckAdmin(user.Id.ToString()))
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
                var cfgjson = JsonCfg.GetJSONData();

                if (IsOwner(user) || DBase.CheckSuperAdmin(user.Id.ToString()))
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

                if (user.Id.Equals(Convert.ToUInt64(cfgjson.BotOwner)))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}
