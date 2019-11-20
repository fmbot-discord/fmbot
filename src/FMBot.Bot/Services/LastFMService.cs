using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
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
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            var tracks = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);
            GlobalVars.LastFMApiCalls++;

            return tracks.Content[0];
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            var recentScrobbles = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);
            GlobalVars.LastFMApiCalls++;

            return recentScrobbles;
        }


        public static string TrackToLinkedString(LastTrack track)
        {
            if (track.Name.IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return TrackToString(track);
            }

            return $"[{track.Name}]({track.Url})\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static string TrackToString(LastTrack track)
        {
            return $"{track.Name}\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            var user = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            GlobalVars.LastFMApiCalls++;

            return user;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName)
        {
            var albumInfo = await this._lastFMClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return albumInfo;
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            var album = await this._lastFMClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return album?.Content?.Images;
        }

        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan,
            int count = 2)
        {
            var topAlbums = await this._lastFMClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topAlbums;
        }

        // Artist info
        public async Task<LastResponse<LastArtist>> GetArtistInfoAsync(string artistName)
        {
            var artistInfo = await this._lastFMClient.Artist.GetInfoAsync(artistName);
            GlobalVars.LastFMApiCalls++;

            return artistInfo;
        }

        // Artist info
        public async Task<LastImageSet> GetArtistImageAsync(string artistName)
        {
            var artist = await this._lastFMClient.Artist.GetInfoAsync(artistName);

            var artist2 = await this._lastFMClient.Artist.GetInfoByMbidAsync(artist.Content.Mbid);
            GlobalVars.LastFMApiCalls++;

            return artist2?.Content?.MainImage;
        }

        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName,
            LastStatsTimeSpan timespan, int count = 2)
        {
            var topArtists = await this._lastFMClient.User.GetTopArtists(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topArtists;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            var lastFMUser = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
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

            if (timespan.Equals("yearly") || timespan.Equals("year") || timespan.Equals("y"))
            {
                return LastStatsTimeSpan.Year;
            }

            if (timespan.Equals("overall") || timespan.Equals("alltime") || timespan.Equals("o") ||
                timespan.Equals("at") || timespan.Equals("a"))
            {
                return LastStatsTimeSpan.Overall;
            }

            return LastStatsTimeSpan.Week;
        }

        public async Task GenerateChartAsync(FMBotChart chart)
        {
            try
            {
                if (!Directory.Exists(GlobalVars.CacheFolder))
                {
                    Directory.CreateDirectory(GlobalVars.CacheFolder);
                }

                // Album mode
                await chart.albums.ParallelForEachAsync(async album =>
                {
                    var albumImages = await GetAlbumImagesAsync(album.ArtistName, album.Name);

                    Bitmap chartImage;

                    if (albumImages?.Large != null)
                    {
                        var url = albumImages.Large.AbsoluteUri;
                        var path = Path.GetFileName(url);

                        if (File.Exists(GlobalVars.CacheFolder + path))
                        {
                            chartImage = new Bitmap(GlobalVars.CacheFolder + path);
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
                                GlobalVars.CacheFolder + path,
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
                        chartImage = new Bitmap(GlobalVars.ImageFolder + "unknown.png");
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

                    chart.images.Add(new ChartImage(chartImage, chart.albums.IndexOf(album)));
                });
            }
            finally
            {
                var imageList =
                    GlobalVars.splitBitmapList(chart.images.OrderBy(o => o.Index).Select(s => s.Image).ToList(),
                        chart.rows);

                var bitmapList = imageList.ToArray().Select(list => GlobalVars.Combine(list)).ToList();

                lock (GlobalVars.charts.SyncRoot)
                {
                    GlobalVars.charts[GlobalVars.GetChartFileName(chart.DiscordUser.Id)] =
                        GlobalVars.Combine(bitmapList, true);
                }

                foreach (var image in bitmapList.ToArray())
                {
                    image.Dispose();
                }
            }
        }
    }
}
