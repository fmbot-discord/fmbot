using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Timer _timer; 
        private readonly Logger.Logger _logger;

        private string trackString = "";

        private bool timerEnabled = false;
        private readonly UserService userService = new UserService();
        private readonly LastFMService lastFMService = new LastFMService();

        public TimerService(DiscordShardedClient client, Logger.Logger logger)
        {
            _logger = logger;

            _timer = new Timer(async _ =>
            {

                string LastFMName = await userService.GetRandomLastFMUserAsync().ConfigureAwait(false);
                if (LastFMName != null)
                {
                    Random random = new Random();
                    int randavmode = random.Next(1, 4);
                    string randmodestring = "";

                    if (randavmode == 1)
                    {
                        randmodestring = "1 - Recent Listens";
                    }
                    else if (randavmode == 2)
                    {
                        randmodestring = "2 - Weekly Albums";
                    }
                    else if (randavmode == 3)
                    {
                        randmodestring = "3 - Overall Albums";
                    }
                    else if (randavmode == 4)
                    {
                        randmodestring = "4 - Default Avatar";
                    }


                    _logger.Log("Changing avatar to mode " + randmodestring);

                    string nulltext = "";

                    try
                    {
                        if (randavmode == 1)
                        {
                            PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(LastFMName).ConfigureAwait(false);
                            LastTrack currentTrack = tracks.Content[0];

                            string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                            LastImageSet AlbumImages = null;

                            if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                            {
                                    // use the censored cover.
                                    try
                                {
                                    UseLocalAvatar(client, AlbumName, ArtistName, LastFMName);
                                    return;
                                }
                                catch (Exception)
                                {
                                    UseDefaultAvatar(client);
                                }
                            }
                            else
                            {
                                AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName).ConfigureAwait(false);

                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                _logger.Log("Changed avatar to: " + trackString);

                                if (AlbumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, AlbumImages.Large.AbsoluteUri);
                                }
                            }

                        }
                        else if (randavmode == 2)
                        {
                            PageResponse<LastAlbum> albums = await lastFMService.GetTopAlbumsAsync(LastFMName, LastStatsTimeSpan.Week, 1).ConfigureAwait(false);
                            LastAlbum currentAlbum = albums.Content[random.Next(albums.Count())];

                            string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                            LastImageSet AlbumImages = null;

                            if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                            {
                                    // use the censored cover.
                                    try
                                {
                                    UseLocalAvatar(client, AlbumName, ArtistName, LastFMName);
                                    return;
                                }
                                catch (Exception)
                                {
                                    UseDefaultAvatar(client);
                                }
                            }
                            else
                            {
                                AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName).ConfigureAwait(false);

                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                _logger.Log("Changed avatar to: " + trackString);

                                if (AlbumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, AlbumImages.Large.AbsoluteUri);
                                }
                            }
                        }
                        else if (randavmode == 3)
                        {
                            PageResponse<LastAlbum> albums = await lastFMService.GetTopAlbumsAsync(LastFMName, LastStatsTimeSpan.Overall, 1).ConfigureAwait(false);
                            LastAlbum currentAlbum = albums.Content[random.Next(albums.Count())];

                            string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                            LastImageSet AlbumImages = null;

                            if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                            {
                                    // use the censored cover.
                                    try
                                {
                                    UseLocalAvatar(client, AlbumName, ArtistName, LastFMName);
                                }
                                catch (Exception)
                                {
                                    UseDefaultAvatar(client);
                                }
                            }
                            else
                            {
                                AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName).ConfigureAwait(false);

                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                _logger.Log("Changed avatar to: " + trackString);

                                if (AlbumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, AlbumImages.Large.AbsoluteUri);
                                }
                            }
                        }
                        else if (randavmode == 4)
                        {
                            UseDefaultAvatar(client);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogException("ChangeFeaturedAvatar", e);
                    }
                }
            },
            null,
            TimeSpan.FromSeconds(Convert.ToDouble(ConfigData.Data.TimerInit)),  // 4) Time that message should fire after the timer is created
            TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

            timerEnabled = true;
        }

        public void Stop() // 6) Example to make the timer stop running
        {
            if (IsTimerActive())
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                timerEnabled = false;
            }
        }

        public void Restart() // 7) Example to restart the timer
        {
            if (!IsTimerActive())
            {
                _timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(ConfigData.Data.TimerInit)), TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.TimerRepeat)));
                timerEnabled = true;
            }
        }

        public async void ChangeToNewAvatar(DiscordShardedClient client, string thumbnail)
        {
            try
            {
                WebRequest request = WebRequest.Create(thumbnail);
                WebResponse response = await request.GetResponseAsync().ConfigureAwait(false);
                using (Stream output = File.Create(GlobalVars.ImageFolder + "newavatar.png"))
                using (Stream input = response.GetResponseStream())
                {
                    input.CopyTo(output);
                    if (File.Exists(GlobalVars.ImageFolder + "newavatar.png"))
                    {
                        File.SetAttributes(GlobalVars.ImageFolder + "newavatar.png", FileAttributes.Normal);
                    }

                    output.Close();
                    input.Close();
                }

                if (File.Exists(GlobalVars.ImageFolder + "newavatar.png"))
                {
                    FileStream fileStream = new FileStream(GlobalVars.ImageFolder + "newavatar.png", FileMode.Open);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream)).ConfigureAwait(false);
                    fileStream.Close();
                }

                await Task.Delay(5000).ConfigureAwait(false);


                ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                SocketGuild guild = client.GetGuild(BroadcastServerID);
                SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                EmbedBuilder builder = new EmbedBuilder();
                SocketSelfUser SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", trackString);

                await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogException("ChangeToNewAvatar", e);
            }
        }

        public async void UseDefaultAvatar(DiscordShardedClient client)
        {
            try
            {
                trackString = "FMBot Default Avatar";
                _logger.Log("Changed avatar to: " + trackString);
                FileStream fileStream = new FileStream(GlobalVars.ImageFolder + "avatar.png", FileMode.Open);
                Image image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image).ConfigureAwait(false);
                fileStream.Close();
            }
            catch (Exception e)
            {
                _logger.LogException("UseDefaultAvatar", e);
            }
        }

        public async void UseLocalAvatar(DiscordShardedClient client, string AlbumName, string ArtistName, string LastFMName)
        {
            try
            {
                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                _logger.Log("Changed avatar to: " + trackString);
                //FileStream fileStream = new FileStream(GlobalVars.CoversFolder + ArtistName + " - " + AlbumName + ".png", FileMode.Open);
                FileStream fileStream = new FileStream(GlobalVars.ImageFolder + "censored.png", FileMode.Open);
                Image image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image).ConfigureAwait(false);
                fileStream.Close();

                await Task.Delay(5000).ConfigureAwait(false);

                ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                SocketGuild guild = client.GetGuild(BroadcastServerID);
                SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                EmbedBuilder builder = new EmbedBuilder();
                SocketSelfUser SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", trackString);

                await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogException("UseLocalAvatar", e);
            }
        }

        public async void UseCustomAvatar(DiscordShardedClient client, string fmquery, string desc, bool important)
        {
            if (important && IsTimerActive())
            {
                Stop();
            }
            else if (!important && !IsTimerActive())
            {
                Restart();
            }

            GlobalVars.FeaturedUserID = "";

            LastfmClient fmclient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);
            try
            {
                PageResponse<LastAlbum> albums = await fmclient.Album.SearchAsync(fmquery, 1, 2).ConfigureAwait(false);
                LastAlbum currentAlbum = albums.Content[0];

                const string nulltext = "";

                string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                try
                {
                    LastResponse<LastAlbum> AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName).ConfigureAwait(false);
                    LastImageSet AlbumImages = AlbumInfo.Content.Images;
                    string AlbumThumbnail = AlbumImages?.Large.AbsoluteUri;
                    string ThumbnailImage = AlbumThumbnail;

                    try
                    {
                        trackString = ArtistName + " - " + AlbumName + Environment.NewLine + desc;
                        _logger.Log("Changed avatar to: " + trackString);
                    }
                    catch (Exception)
                    {
                        try
                        {
                            trackString = desc;
                            _logger.Log("Changed avatar to: " + trackString);
                        }
                        catch (Exception e)
                        {
                            _logger.LogException("UseCustomAvatar", e);
                            UseDefaultAvatar(client);
                            trackString = "Unable to get information for this album cover avatar.";
                        }
                    }

                    ChangeToNewAvatar(client, ThumbnailImage);
                }
                catch (Exception e)
                {
                    _logger.LogException("UseCustomAvatar", e);
                }

            }
            catch (Exception e)
            {
                _logger.LogException("UseCustomAvatar", e);

                UseDefaultAvatar(client);
            }
        }

        public async void UseCustomAvatarFromLink(DiscordShardedClient client, string link, string desc, bool important)
        {
            if (important && IsTimerActive())
            {
                Stop();
            }
            else if (!important && !IsTimerActive())
            {
                Restart();
            }

            GlobalVars.FeaturedUserID = "";

            try
            {
                trackString = desc;
                _logger.Log("Changed avatar to: " + trackString);

                if (!string.IsNullOrWhiteSpace(link))
                {
                    ChangeToNewAvatar(client, link);
                }
            }
            catch (Exception e)
            {
                _logger.LogException("UseCustomAvatarFromLink", e);
            }
        }

        public string GetTrackString()
        {
            return trackString;
        }

        public bool IsTimerActive()
        {
            return timerEnabled;
        }
    }
}
