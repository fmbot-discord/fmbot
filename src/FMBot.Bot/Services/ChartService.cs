using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using IF.Lastfm.Core.Api;
using Microsoft.EntityFrameworkCore.Internal;

namespace FMBot.Bot.Services
{
    public class ChartService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        public async Task GenerateChartAsync(ChartSettings chart)
        {
            try
            {
                if (!Directory.Exists(FMBotUtil.GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(FMBotUtil.GlobalVars.CacheFolder);
                }

                // Album mode
                await chart.Albums.ParallelForEachAsync(async album =>
                {
                    var localAlbumId = CalculateMd5Hash(album.Url.LocalPath);

                    Bitmap chartImage;
                    var validImage = true;

                    var fileName = localAlbumId + ".png";
                    var localPath = FMBotUtil.GlobalVars.CacheFolder + fileName;

                    if (File.Exists(localPath))
                    {
                        chartImage = new Bitmap(localPath);
                    }
                    else
                    {
                        var albumInfo = await this._lastFMClient.Album.GetInfoAsync(album.ArtistName, album.Name);
                        Statistics.LastfmApiCalls.Inc();

                        var albumImages = albumInfo?.Content?.Images;

                        if (albumImages?.Large != null)
                        {

                            var url = albumImages.Large.AbsoluteUri;

                            Bitmap bitmap;
                            try
                            {
                                var request = WebRequest.Create(url);
                                using var response = await request.GetResponseAsync();
                                await using var responseStream = response.GetResponseStream();

                                bitmap = new Bitmap(responseStream);
                            }
                            catch
                            {
                                bitmap = new Bitmap(FMBotUtil.GlobalVars.ImageFolder + "loading-error.png");
                                validImage = false;
                            }

                            chartImage = bitmap;

                            if (validImage)
                            {
                                await using var memoryStream = new MemoryStream();
                                await using var fileStream = new FileStream(
                                    localPath,
                                    FileMode.Create,
                                    FileAccess.ReadWrite);

                                bitmap.Save(memoryStream, ImageFormat.Png);

                                var bytes = memoryStream.ToArray();
                                await fileStream.WriteAsync(bytes, 0, bytes.Length);
                                await fileStream.DisposeAsync();
                            }
                        }
                        else
                        {
                            chartImage = new Bitmap(FMBotUtil.GlobalVars.ImageFolder + "unknown.png");
                            validImage = false;
                        }
                    }
                    
                    if (chart.TitlesEnabled)
                    {
                        try
                        {
                            using var graphics = Graphics.FromImage(chartImage);

                            graphics.DrawColorString(
                                chartImage,
                                album.ArtistName,
                                new Font("Arial", 8.0f, FontStyle.Bold),
                                new PointF(2.0f, 2.0f));

                            graphics.DrawColorString(
                                chartImage,
                                album.Name,
                                new Font("Arial", 8.0f, FontStyle.Bold),
                                new PointF(2.0f, 12.0f));
                        }
                        catch (Exception e)
                        {
                            // TODO: Find out why this bugs on certain album images (rare)
                        }
                    }

                    chart.ChartImages.Add(new ChartImage(chartImage, chart.Albums.IndexOf(album), validImage));
                });
            }
            finally
            {
                var imageList =
                    FMBotUtil.GlobalVars.splitBitmapList(
                        chart.ChartImages
                            .OrderBy(o => o.Index)
                            .Where(w => !chart.SkipArtistsWithoutImage || w.ValidImage)
                            .Take(chart.ImagesNeeded)
                            .Select(s => s.Image)
                            .ToList(),
                        chart.Height);

                var bitmapList = imageList.Select(list => FMBotUtil.GlobalVars.Combine(list)).ToList();

                lock (FMBotUtil.GlobalVars.charts.SyncRoot)
                {
                    FMBotUtil.GlobalVars.charts[FMBotUtil.GlobalVars.GetChartFileName(chart.DiscordUser.Id)] =
                        FMBotUtil.GlobalVars.Combine(bitmapList, true);
                }

                foreach (var image in bitmapList.ToArray())
                {
                    image.Dispose();
                }
            }
        }

        private static string CalculateMd5Hash(string input)
        {
            var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hash = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            for (var i = 0; i < hash.Length; i++)
            {
                sb.Append(i.ToString("X2"));
            }
            return sb.ToString();
        }

        public ChartSettings SetExtraSettings(ChartSettings currentChartSettings, string[] extraOptions)
        {
            var chartSettings = currentChartSettings;

            if (extraOptions.Contains("notitles") || extraOptions.Contains("nt"))
            {
                chartSettings.TitlesEnabled = false;
            }

            if (extraOptions.Contains("skipemptyimages") ||
                extraOptions.Contains("skipemptyalbums") ||
                extraOptions.Contains("skipalbums") ||
                extraOptions.Contains("skip") ||
                extraOptions.Contains("s"))
            {
                chartSettings.SkipArtistsWithoutImage = true;
            }

            return chartSettings;
        }
    }
}
