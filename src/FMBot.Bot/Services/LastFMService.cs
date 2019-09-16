using FMBot.Bot.Extensions;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bot.Logger.Interfaces;
using FMBot.Bot.Configurations;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Services
{
    internal class LastFMService
    {
        private readonly ILogger _logger;

        public LastfmClient lastfmClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1).ConfigureAwait(false);

            return tracks.Content[0];
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            return await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count).ConfigureAwait(false);
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            try
            {
                return await lastfmClient.User.GetInfoAsync(lastFMUserName).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogException("GetUserInfoAsync", e);

            }
            return null;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName)
        {
            return await lastfmClient.Album.GetInfoAsync(artistName, albumName).ConfigureAwait(false);
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            LastResponse<LastAlbum> album = await lastfmClient.Album.GetInfoAsync(artistName, albumName).ConfigureAwait(false);

            return album?.Content?.Images;
        }

        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            return await lastfmClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count).ConfigureAwait(false);
        }

        // Artist info
        public async Task<LastResponse<LastArtist>> GetArtistInfoAsync(string artistName)
        {
            return await lastfmClient.Artist.GetInfoAsync(artistName).ConfigureAwait(false);
        }

        // Artist info
        public async Task<LastImageSet> GetArtistImageAsync(string artistName)
        {
            LastResponse<LastArtist> artist = await lastfmClient.Artist.GetInfoAsync(artistName).ConfigureAwait(false);

            var artist2 = await lastfmClient.Artist.GetInfoByMbidAsync(artist.Content.Mbid).ConfigureAwait(false);

            return artist2?.Content?.MainImage;
        }

        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            return await lastfmClient.User.GetTopArtists(lastFMUserName, timespan, 1, count).ConfigureAwait(false);
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            LastResponse<LastUser> lastFMUser = await lastfmClient.User.GetInfoAsync(lastFMUserName).ConfigureAwait(false);

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

                if (chart.mode == 0)
                {
                    for (int albumIndex = 0; albumIndex < chart.albums.Count(); ++albumIndex)
                    {
                        LastAlbum track = chart.albums.Content[albumIndex];

                        string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                        string AlbumName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;

                        LastImageSet albumImages = await GetAlbumImagesAsync(ArtistName, AlbumName).ConfigureAwait(false);

                        Bitmap cover;

                        if (albumImages?.Large != null)
                        {
                            string url = albumImages.Large.AbsoluteUri;
                            string path = Path.GetFileName(url);

                            if (File.Exists(GlobalVars.CacheFolder + path))
                            {
                                cover = new Bitmap(GlobalVars.CacheFolder + path);

                            }
                            else
                            {
                                WebRequest request = WebRequest.Create(url);
                                using (WebResponse response = await request.GetResponseAsync().ConfigureAwait(false))
                                {
                                    using (Stream responseStream = response.GetResponseStream())
                                    {
                                        Bitmap bitmap = new Bitmap(responseStream);

                                        cover = bitmap;
                                        using (MemoryStream memory = new MemoryStream())
                                        {
                                            using (FileStream fs = new FileStream(GlobalVars.CacheFolder + path, FileMode.Create, FileAccess.ReadWrite))
                                            {
                                                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                                                byte[] bytes = memory.ToArray();
                                                fs.Write(bytes, 0, bytes.Length);
                                            }
                                        }

                                    }
                                }
                            }
                        }
                        else
                        {
                            cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                        }

                        if (chart.titles)
                        {
                            Graphics text = Graphics.FromImage(cover);
                            text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                            text.DrawColorString(cover, AlbumName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 12.0f));
                        }

                        chart.images.Add(cover);
                    }
                }
                else if (chart.mode == 1)
                {
                    PageResponse<LastArtist> artists = await GetTopArtistsAsync(chart.LastFMName, timespan, chart.max).ConfigureAwait(false);
                    for (int al = 0; al < chart.max; ++al)
                    {
                        LastArtist artist = artists.Content[al];

                        string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                        LastImageSet artistImage = await GetArtistImageAsync(ArtistName).ConfigureAwait(false);

                        Bitmap cover;

                        if (artistImage?.Large != null)
                        {
                            string url = artistImage.Large.AbsoluteUri;
                            string path = Path.GetFileName(url);

                            if (File.Exists(GlobalVars.CacheFolder + path))
                            {
                                cover = new Bitmap(GlobalVars.CacheFolder + path);

                            }
                            else
                            {
                                WebRequest request = WebRequest.Create(url);
                                using (WebResponse response = await request.GetResponseAsync().ConfigureAwait(false))
                                {
                                    using (Stream responseStream = response.GetResponseStream())
                                    {
                                        Bitmap bitmap = new Bitmap(responseStream);

                                        cover = bitmap;
                                        using (MemoryStream memory = new MemoryStream())
                                        {
                                            using (FileStream fs = new FileStream(GlobalVars.CacheFolder + path, FileMode.Create, FileAccess.ReadWrite))
                                            {
                                                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                                                byte[] bytes = memory.ToArray();
                                                fs.Write(bytes, 0, bytes.Length);
                                            }
                                        }

                                    }
                                }
                            }
                        }
                        else
                        {
                            cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                        }

                        if (chart.titles)
                        {
                            Graphics text = Graphics.FromImage(cover);
                            text.DrawColorString(cover, ArtistName, new Font("Arial", 8.0f, FontStyle.Bold), new PointF(2.0f, 2.0f));
                        }

                        chart.images.Add(cover);

                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogException("GenerateChartAsync", e);
            }
            finally
            {
                List<List<Bitmap>> ImageLists = GlobalVars.splitBitmapList(chart.images, chart.rows);

                List<Bitmap> BitmapList = new List<Bitmap>();

                foreach (List<Bitmap> list in ImageLists.ToArray())
                {
                    //combine them into one image
                    Bitmap stitchedRow = GlobalVars.Combine(list);
                    BitmapList.Add(stitchedRow);
                }

                lock (GlobalVars.charts.SyncRoot)
                {
                    GlobalVars.charts[GlobalVars.GetChartFileName(chart.DiscordUser.Id)] = GlobalVars.Combine(BitmapList, true);
                }

                foreach (Bitmap image in BitmapList.ToArray())
                {
                    image.Dispose();
                }
            }
        }
    }
}