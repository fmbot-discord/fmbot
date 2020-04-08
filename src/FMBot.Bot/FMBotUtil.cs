using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Bot
{
    // TODO: get rid of this file
    public static class FMBotUtil
    {
        public static class GlobalVars
        {
            // TODO: Move this to database
            public static readonly IReadOnlyList<CensoredAlbum> CensoredAlbums = new List<CensoredAlbum>
            {
                new CensoredAlbum("Death Grips", "No Love Deep Web"),
                new CensoredAlbum("ミドリ(Midori)", "あらためまして、はじめまして、ミドリです。(aratamemashite hajimemashite midori desu)"),
                new CensoredAlbum("Midori", "ratamemashite hajimemashite midori desu"),
                new CensoredAlbum("ミドリ", "あらためまして、はじめまして、ミドリです"),
                new CensoredAlbum("Xiu Xiu", "A Promise"),
                new CensoredAlbum("Carcass","Reek of Putrefaction"),
                new CensoredAlbum("Cattle Decapitation", "Human Jerky"),
                new CensoredAlbum("Niki Istrefi", "EUROMANTIC001"),
                new CensoredAlbum("Last Days Of Humanity", "Hymns Of Indigestible Suppuration"),
                new CensoredAlbum("Last Days Of Humanity", "The Xtc Of Swallowing L.D.O.H. Feaces"),
                new CensoredAlbum("Last Days Of Humanity", "The Heart of Gore"),
                new CensoredAlbum("Last Days Of Humanity", "Human Atrocity"),
                new CensoredAlbum("Last Days Of Humanity", "Rennes in Blood"),
                new CensoredAlbum("Last Days Of Humanity", "Goresurrection"),
                new CensoredAlbum("Last Days Of Humanity", "Putrefaction In Progress"),
                new CensoredAlbum("Last Days Of Humanity", "The Sound of Rancid Juices Sloshing Around Your Coffin"),
                new CensoredAlbum("Last Days Of Humanity", "In Advanced Haemorrhaging Conditions"),
                new CensoredAlbum("Last Days Of Humanity", "Extreme Experience Of Inhuman Motivations"),
                new CensoredAlbum("Cannibal Corpse", "Tomb of the Mutilated"),
                new CensoredAlbum("Regurgitate", "Carnivorous Erection")
            };

            // TODO: Move this somewhere else
            private static readonly string BasePath = AppDomain.CurrentDomain.BaseDirectory;
            public static readonly string CacheFolder = BasePath + "cache/";
            public static string ImageFolder = BasePath + "resources/images/";
            public static string FeaturedUserID = "";
            public static Hashtable charts = new Hashtable();

            public static string GetChartFileName(ulong id)
            {
                return CacheFolder + id + "-chart.png";
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
                var upTime = ((double)ticks) / Stopwatch.Frequency;
                return TimeSpan.FromSeconds(upTime);
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
                        g.Clear(Color.Black);

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
                    finalImage?.Dispose();

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
        }
    }
}
