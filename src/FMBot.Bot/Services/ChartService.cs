using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using IF.Lastfm.Core.Api;
using Microsoft.EntityFrameworkCore.Internal;

namespace FMBot.Bot.Services
{
    public class ChartService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        public async Task GenerateChartAsync(LastFMModels.FMBotChart chart)
        {
            try
            {
                if (!Directory.Exists(FMBotUtil.GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(FMBotUtil.GlobalVars.CacheFolder);
                }

                // Album mode
                await chart.albums.ParallelForEachAsync(async album =>
                {
                    var albumInfo = await this._lastFMClient.Album.GetInfoAsync(album.ArtistName, album.Name);
                    FMBotUtil.GlobalVars.LastFMApiCalls++;

                    var albumImages = albumInfo?.Content?.Images;

                    Bitmap chartImage;

                    if (albumImages?.Large != null)
                    {
                        var url = albumImages.Large.AbsoluteUri;
                        var path = Path.GetFileName(url);

                        if (File.Exists(FMBotUtil.GlobalVars.CacheFolder + path))
                        {
                            chartImage = new Bitmap(FMBotUtil.GlobalVars.CacheFolder + path);
                        }
                        else
                        {
                            var request = WebRequest.Create(url);
                            using var response = await request.GetResponseAsync();
                            await using var responseStream = response.GetResponseStream();

                            var bitmap = new Bitmap(responseStream);
                            chartImage = bitmap;

                            await using var memoryStream = new MemoryStream();
                            await using var fileStream = new FileStream(
                                FMBotUtil.GlobalVars.CacheFolder + path,
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
                    }

                    if (chart.titles)
                    {
                        try
                        {
                            using Graphics graphics = Graphics.FromImage(chartImage);

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

                    chart.images.Add(new LastFMModels.ChartImage(chartImage, chart.albums.IndexOf(album)));
                });
            }
            finally
            {
                var imageList =
                    FMBotUtil.GlobalVars.splitBitmapList(chart.images.OrderBy(o => o.Index).Select(s => s.Image).ToList(),
                        chart.rows);

                var bitmapList = imageList.ToArray().Select(list => FMBotUtil.GlobalVars.Combine(list)).ToList();

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
    }
}
