using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dasync.Collections;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore.Internal;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Bot.Services
{
    internal class LastFMService
    {
        public readonly LastfmClient LastfmClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            PageResponse<LastTrack> tracks = await LastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);
            GlobalVars.LastFMApiCalls++;

            return tracks.Content[0];
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            var recentScrobbles = await LastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);
            GlobalVars.LastFMApiCalls++;

            return recentScrobbles;
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            var user = await LastfmClient.User.GetInfoAsync(lastFMUserName);
            GlobalVars.LastFMApiCalls++;

            return user;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName)
        {
            var albumInfo = await LastfmClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return albumInfo;
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            LastResponse<LastAlbum> album = await LastfmClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return album?.Content?.Images;
        }

        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            var topAlbums = await LastfmClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topAlbums;
        }

        // Artist info
        public async Task<LastResponse<LastArtist>> GetArtistInfoAsync(string artistName)
        {
            var artistInfo = await LastfmClient.Artist.GetInfoAsync(artistName);
            GlobalVars.LastFMApiCalls++;

            return artistInfo;
        }

        // Artist info
        public async Task<LastImageSet> GetArtistImageAsync(string artistName)
        {
            LastResponse<LastArtist> artist = await LastfmClient.Artist.GetInfoAsync(artistName);

            var artist2 = await LastfmClient.Artist.GetInfoByMbidAsync(artist.Content.Mbid);
            GlobalVars.LastFMApiCalls++;

            return artist2?.Content?.MainImage;
        }

        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            var topArtists = await LastfmClient.User.GetTopArtists(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topArtists;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            LastResponse<LastUser> lastFMUser = await LastfmClient.User.GetInfoAsync(lastFMUserName);
            GlobalVars.LastFMApiCalls++;

            return lastFMUser.Success;
        }

        public LastStatsTimeSpan GetLastStatsTimeSpan(ChartTimePeriod timePeriod)
        {
            switch (timePeriod)
            {
                case ChartTimePeriod.Weekly:
                    return LastStatsTimeSpan.Week;
                case ChartTimePeriod.Monthly:
                    return LastStatsTimeSpan.Month;
                case ChartTimePeriod.Yearly:
                    return LastStatsTimeSpan.Year;
                case ChartTimePeriod.AllTime:
                    return LastStatsTimeSpan.Overall;
                default:
                    return LastStatsTimeSpan.Week;
            }
        }

        public LastStatsTimeSpan StringToLastStatsTimeSpan(string timespan)
        {
            if (timespan.Equals("monthly") || timespan.Equals("month") || timespan.Equals("m"))
            {
                return LastStatsTimeSpan.Month;
            }
            else if (timespan.Equals("yearly") || timespan.Equals("year") || timespan.Equals("y"))
            {
                return LastStatsTimeSpan.Year;
            }
            else if (timespan.Equals("overall") || timespan.Equals("alltime") || timespan.Equals("o") || timespan.Equals("at"))
            {
                return LastStatsTimeSpan.Overall;
            }

            return LastStatsTimeSpan.Week;
        }

        public async Task GenerateChartAsync(FMBotChart chart)
        {
            try
            {
                LastStatsTimeSpan timespan = LastStatsTimeSpan.Week;

                const string nulltext = "[undefined]";

                if (!Directory.Exists(GlobalVars.CacheFolder))
                    Directory.CreateDirectory(GlobalVars.CacheFolder);

                // Album mode
                await chart.albums.ParallelForEachAsync(async album =>
                {
                    string artistName = string.IsNullOrWhiteSpace(album.ArtistName) ? nulltext : album.ArtistName;
                    string albumName = string.IsNullOrWhiteSpace(album.Name) ? nulltext : album.Name;

                    LastImageSet albumImages = await GetAlbumImagesAsync(artistName, albumName);

                    Bitmap chartImage;

                    if (albumImages?.Large != null)
                    {
                        string url = albumImages.Large.AbsoluteUri;
                        string path = Path.GetFileName(url);

                        if (File.Exists(GlobalVars.CacheFolder + path))
                        {
                            chartImage = new Bitmap(GlobalVars.CacheFolder + path);
                        }
                        else
                        {
                            WebRequest request = WebRequest.Create(url);
                            using WebResponse response = await request.GetResponseAsync();
                            await using Stream responseStream = response.GetResponseStream();

                            Bitmap bitmap = new Bitmap(responseStream);

                            chartImage = bitmap;
                            await using MemoryStream memory = new MemoryStream();
                            await using FileStream fs = new FileStream(GlobalVars.CacheFolder + path, FileMode.Create, FileAccess.ReadWrite);

                            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);

                            byte[] bytes = memory.ToArray();
                            fs.Write(bytes, 0, bytes.Length);
                        }
                    }
                    else
                    {
                        chartImage = new Bitmap(GlobalVars.ImageFolder + "unknown.png");
                    }

                    if (chart.titles)
                    {
                        Graphics text = Graphics.FromImage(chartImage);
                        text.DrawColorString(chartImage, artistName, new Font("Arial", 8.0f, FontStyle.Bold),
                            new PointF(2.0f, 2.0f));
                        text.DrawColorString(chartImage, albumName, new Font("Arial", 8.0f, FontStyle.Bold),
                            new PointF(2.0f, 12.0f));
                    }

                    chart.images.Add(new ChartImage(chartImage, chart.albums.IndexOf(album)));

                });
            }
            catch (Exception e)
            {
                //_logger.LogException("GenerateChartAsync", e);
            }
            finally
            {
                List<List<Bitmap>> imageList = GlobalVars.splitBitmapList(chart.images.OrderBy(o => o.Index).Select(s => s.Image).ToList(), chart.rows);

                List<Bitmap> bitmapList = new List<Bitmap>();
                foreach (List<Bitmap> list in imageList.ToArray())
                {
                    //combine them into one image
                    Bitmap stitchedRow = GlobalVars.Combine(list);
                    bitmapList.Add(stitchedRow);
                }

                lock (GlobalVars.charts.SyncRoot)
                {
                    GlobalVars.charts[GlobalVars.GetChartFileName(chart.DiscordUser.Id)] = GlobalVars.Combine(bitmapList, true);
                }

                foreach (Bitmap image in bitmapList.ToArray())
                {
                    image.Dispose();
                }
            }
        }
    }
}
