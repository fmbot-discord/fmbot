using Discord;
using Discord.WebSocket;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    class LastFMService
    {
        public static JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        public LastfmClient lastfmClient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

        public class FMBotChart
        {
            public string time;
            public string LastFMName;
            public int max;
            public int rows;
            public List<Bitmap> images;
            public IUser DiscordUser;
            public DiscordSocketClient disclient;
            public int mode;
            public bool titles;
        }


        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);

            LastTrack track = tracks.Content.ElementAt(0);

            return track;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);

            return tracks;
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            try
            {
                LastResponse<LastUser> userInfo = await lastfmClient.User.GetInfoAsync(lastFMUserName);

                return userInfo;
            }
            catch (Exception e)
            {
                ExceptionReporter.ReportException(null, e);
            }
            return null;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName)
        {
            LastResponse<LastAlbum> album = await lastfmClient.Album.GetInfoAsync(artistName, albumName);

            return album;
        }


        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            LastResponse<LastAlbum> album = await lastfmClient.Album.GetInfoAsync(artistName, albumName);

            LastImageSet images = album != null ? album.Content != null ? album.Content.Images != null ? album.Content.Images : null : null : null;

            return images;
        }


        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            PageResponse<LastAlbum> albums = await lastfmClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count);

            return albums;
        }


        // Artist info
        public async Task<LastResponse<LastArtist>> GetArtistInfoAsync(string artistName)
        {
            LastResponse<LastArtist> artist = await lastfmClient.Artist.GetInfoAsync(artistName);

            return artist;
        }


        // Artist info
        public async Task<LastImageSet> GetArtistImageAsync(string artistName)
        {
            LastResponse<LastArtist> artist = await lastfmClient.Artist.GetInfoAsync(artistName);

            LastImageSet image = artist != null ? artist.Content != null ? artist.Content.MainImage : null : null;

            return image;
        }


        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            PageResponse<LastArtist> artists = await lastfmClient.User.GetTopArtists(lastFMUserName, timespan, 1, count);

            return artists;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            LastResponse<LastUser> lastFMUser = await lastfmClient.User.GetInfoAsync(lastFMUserName);

            return lastFMUser.Success;
        }


        public async Task GenerateChartAsync(FMBotChart chart)
        {
            try
            {
                LastStatsTimeSpan timespan = LastStatsTimeSpan.Week;

                if (chart.time.Equals("weekly") || chart.time.Equals("week") || chart.time.Equals("w"))
                {
                    timespan = LastStatsTimeSpan.Week;
                }
                else if (chart.time.Equals("monthly") || chart.time.Equals("month") || chart.time.Equals("m"))
                {
                    timespan = LastStatsTimeSpan.Month;
                }
                else if (chart.time.Equals("yearly") || chart.time.Equals("year") || chart.time.Equals("y"))
                {
                    timespan = LastStatsTimeSpan.Year;
                }
                else if (chart.time.Equals("overall") || chart.time.Equals("alltime") || chart.time.Equals("o") || chart.time.Equals("at"))
                {
                    timespan = LastStatsTimeSpan.Overall;
                }

                string nulltext = "[undefined]";

                if (chart.mode == 0)
                {
                    PageResponse<LastAlbum> albums = await GetTopAlbumsAsync(chart.LastFMName, timespan, chart.max);

                    for (int al = 0; al < chart.max; ++al)
                    {
                        LastAlbum track = albums.Content.ElementAt(al);

                        string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                        string AlbumName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;

                        var albumImages = await GetAlbumImagesAsync(ArtistName, AlbumName);

                        Bitmap cover;

                        if (albumImages != null && albumImages.Large != null)
                        {
                            WebRequest request = WebRequest.Create(albumImages.Large.AbsoluteUri.ToString());
                            using (WebResponse response = request.GetResponse())
                            {
                                using (Stream responseStream = response.GetResponseStream())
                                {
                                    cover = new Bitmap(responseStream);
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
                    PageResponse<LastArtist> artists = await GetTopArtistsAsync(chart.LastFMName, timespan, chart.max);
                    for (int al = 0; al < chart.max; ++al)
                    {
                        LastArtist artist = artists.Content.ElementAt(al);

                        string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                        var artistImage = await GetArtistImageAsync(ArtistName);

                        Bitmap cover;

                        if (artistImage != null && artistImage.Large != null)
                        {
                            WebRequest request = WebRequest.Create(artistImage.Large.AbsoluteUri.ToString());
                            using (WebResponse response = request.GetResponse())
                            {
                                using (Stream responseStream = response.GetResponseStream())
                                {
                                    cover = new Bitmap(responseStream);
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
                ExceptionReporter.ReportException(chart.disclient, e);
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

                Bitmap stitchedImage = GlobalVars.Combine(BitmapList, true);

                foreach (Bitmap image in BitmapList.ToArray())
                {
                    image.Dispose();
                }

                using (MemoryStream memory = new MemoryStream())
                {
                    using (FileStream fs = new FileStream(GlobalVars.CacheFolder + chart.DiscordUser.Id + "-chart.png", FileMode.Create, FileAccess.ReadWrite))
                    {
                        stitchedImage.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                        byte[] bytes = memory.ToArray();
                        fs.Write(bytes, 0, bytes.Length);
                    }
                }
            }
        }
    }
}
