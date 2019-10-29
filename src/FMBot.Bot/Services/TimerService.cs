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
using FMBot.Bot.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Logger.Logger _logger;
        private readonly Timer _timer;
        private readonly LastFMService lastFMService = new LastFMService();
        private readonly UserService userService = new UserService();

        private bool timerEnabled;

        private string trackString = "";

        public TimerService(DiscordShardedClient client, Logger.Logger logger)
        {
            this._logger = logger;

            this._timer = new Timer(async _ =>
                {
                    var random = new Random();
                    var randomAvatarMode = random.Next(1, 4);
                    var randomAvatarModeDesc = "";

                    if (randomAvatarMode == 1)
                    {
                        randomAvatarModeDesc = "Recent Listens";
                    }
                    else if (randomAvatarMode == 2)
                    {
                        randomAvatarModeDesc = "Weekly Albums";
                    }
                    else if (randomAvatarMode == 3)
                    {
                        randomAvatarModeDesc = "Overall Albums";
                    }
                    else if (randomAvatarMode == 4)
                    {
                        randomAvatarModeDesc = "Default Avatar";
                    }

                    this._logger.Log("Changing avatar to mode " + randomAvatarModeDesc);

                    var nulltext = "";
                    try
                    {
                        switch (randomAvatarMode)
                        {
                            case 1:
                                {
                                    var lastFMUserName = await this.userService.GetRandomLastFMUserAsync();
                                    var tracks = await this.lastFMService.GetRecentScrobblesAsync(lastFMUserName, 25);
                                    var trackList = tracks.Select(s => new LastFMModels.Album(s.ArtistName, s.AlbumName));
                                    var currentTrack = trackList.First(f => !GlobalVars.CensoredAlbums.Contains(f));

                                    var albumImages = await this.lastFMService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                                    this.trackString = $"Featured album: {currentTrack.AlbumName} \n" +
                                                       $"by **{currentTrack.ArtistName}** \n \n" +
                                                       $"User: {lastFMUserName} ({randomAvatarModeDesc})";

                                    this._logger.Log("Changing avatar to: " + this.trackString);

                                    if (albumImages?.Large != null)
                                    {
                                        ChangeToNewAvatar(client, albumImages.Large.AbsoluteUri);
                                    }

                                    break;
                                }
                            case 2:
                                {
                                    var lastFMUserName = await this.userService.GetRandomLastFMUserAsync();
                                    var albums =
                                        await this.lastFMService.GetTopAlbumsAsync(lastFMUserName, LastStatsTimeSpan.Week, 1);
                                    var currentAlbum = albums.Content[random.Next(albums.Count())];

                                    var ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName)
                                        ? nulltext
                                        : currentAlbum.ArtistName;
                                    var AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name)
                                        ? nulltext
                                        : currentAlbum.Name;

                                    LastImageSet AlbumImages = null;

                                    if (GlobalVars.CensoredAlbums.Contains(
                                        new KeyValuePair<string, string>(ArtistName, AlbumName)))
                                    {
                                        // use the censored cover.
                                        try
                                        {
                                            UseLocalAvatar(client, AlbumName, ArtistName, lastFMUserName);
                                        }
                                        catch (Exception)
                                        {
                                            UseDefaultAvatar(client);
                                        }
                                    }
                                    else
                                    {
                                        AlbumImages = await this.lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                                        this.trackString =
                                            ArtistName + " - " + AlbumName + Environment.NewLine + lastFMUserName;
                                        this._logger.Log("Changed avatar to: " + this.trackString);

                                        if (AlbumImages?.Large != null)
                                        {
                                            ChangeToNewAvatar(client, AlbumImages.Large.AbsoluteUri);
                                        }
                                    }

                                    break;
                                }
                            case 3:
                                {
                                    var lastFMUserName = await this.userService.GetRandomLastFMUserAsync();
                                    var albums =
                                        await this.lastFMService.GetTopAlbumsAsync(lastFMUserName, LastStatsTimeSpan.Overall,
                                            1);
                                    var currentAlbum = albums.Content[random.Next(albums.Count())];

                                    var ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName)
                                        ? nulltext
                                        : currentAlbum.ArtistName;
                                    var AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name)
                                        ? nulltext
                                        : currentAlbum.Name;

                                    LastImageSet AlbumImages = null;

                                    if (GlobalVars.CensoredAlbums.Contains(
                                        new KeyValuePair<string, string>(ArtistName, AlbumName)))
                                    {
                                        // use the censored cover.
                                        try
                                        {
                                            UseLocalAvatar(client, AlbumName, ArtistName, lastFMUserName);
                                        }
                                        catch (Exception)
                                        {
                                            UseDefaultAvatar(client);
                                        }
                                    }
                                    else
                                    {
                                        AlbumImages = await this.lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                                        this.trackString =
                                            ArtistName + " - " + AlbumName + Environment.NewLine + lastFMUserName;
                                        this._logger.Log("Changed avatar to: " + this.trackString);

                                        if (AlbumImages?.Large != null)
                                        {
                                            ChangeToNewAvatar(client, AlbumImages.Large.AbsoluteUri);
                                        }
                                    }

                                    break;
                                }
                            case 4:
                                UseDefaultAvatar(client);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        this._logger.LogException("ChangeFeaturedAvatar", e);
                    }
                },
                null,
                TimeSpan.FromSeconds(
                    Convert.ToDouble(ConfigData.Data
                        .TimerInit)), // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(
                    Convert.ToDouble(ConfigData.Data
                        .TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

            this.timerEnabled = true;
        }

        public void Stop() // 6) Example to make the timer stop running
        {
            if (IsTimerActive())
            {
                this._timer.Change(Timeout.Infinite, Timeout.Infinite);
                this.timerEnabled = false;
            }
        }

        public void Restart() // 7) Example to restart the timer
        {
            if (!IsTimerActive())
            {
                this._timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(ConfigData.Data.TimerInit)),
                    TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.TimerRepeat)));
                this.timerEnabled = true;
            }
        }

        public async void ChangeToNewAvatar(DiscordShardedClient client, string thumbnail)
        {
            try
            {
                var request = WebRequest.Create(thumbnail);
                var response = await request.GetResponseAsync();
                using (Stream output = File.Create(GlobalVars.ImageFolder + "newavatar.png"))
                using (var input = response.GetResponseStream())
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
                    var fileStream = new FileStream(GlobalVars.ImageFolder + "newavatar.png", FileMode.Open);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream));
                    fileStream.Close();
                }

                await Task.Delay(5000);


                var BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                var guild = client.GetGuild(BroadcastServerID);
                var channel = guild.GetTextChannel(BroadcastChannelID);

                var builder = new EmbedBuilder();
                var SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", this.trackString);

                await channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                this._logger.LogException("ChangeToNewAvatar", e);
            }
        }

        public async void UseDefaultAvatar(DiscordShardedClient client)
        {
            try
            {
                this.trackString = "FMBot Default Avatar";
                this._logger.Log("Changed avatar to: " + this.trackString);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "avatar.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();
            }
            catch (Exception e)
            {
                this._logger.LogException("UseDefaultAvatar", e);
            }
        }

        public async void UseLocalAvatar(DiscordShardedClient client, string AlbumName, string ArtistName,
            string LastFMName)
        {
            try
            {
                this.trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                this._logger.Log("Changed avatar to: " + this.trackString);
                //FileStream fileStream = new FileStream(GlobalVars.CoversFolder + ArtistName + " - " + AlbumName + ".png", FileMode.Open);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "censored.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();

                await Task.Delay(5000);

                var BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                var guild = client.GetGuild(BroadcastServerID);
                var channel = guild.GetTextChannel(BroadcastChannelID);

                var builder = new EmbedBuilder();
                var SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", this.trackString);

                await channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                this._logger.LogException("UseLocalAvatar", e);
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

            var fmclient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);
            try
            {
                var albums = await fmclient.Album.SearchAsync(fmquery, 1, 2);
                var currentAlbum = albums.Content[0];

                const string nulltext = "";

                var ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName)
                    ? nulltext
                    : currentAlbum.ArtistName;
                var AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                try
                {
                    var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                    var AlbumImages = AlbumInfo.Content.Images;
                    var AlbumThumbnail = AlbumImages?.Large.AbsoluteUri;
                    var ThumbnailImage = AlbumThumbnail;

                    try
                    {
                        this.trackString = ArtistName + " - " + AlbumName + Environment.NewLine + desc;
                        this._logger.Log("Changed avatar to: " + this.trackString);
                    }
                    catch (Exception)
                    {
                        try
                        {
                            this.trackString = desc;
                            this._logger.Log("Changed avatar to: " + this.trackString);
                        }
                        catch (Exception e)
                        {
                            this._logger.LogException("UseCustomAvatar", e);
                            UseDefaultAvatar(client);
                            this.trackString = "Unable to get information for this album cover avatar.";
                        }
                    }

                    ChangeToNewAvatar(client, ThumbnailImage);
                }
                catch (Exception e)
                {
                    this._logger.LogException("UseCustomAvatar", e);
                }
            }
            catch (Exception e)
            {
                this._logger.LogException("UseCustomAvatar", e);

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
                this.trackString = desc;
                this._logger.Log("Changed avatar to: " + this.trackString);

                if (!string.IsNullOrWhiteSpace(link))
                {
                    ChangeToNewAvatar(client, link);
                }
            }
            catch (Exception e)
            {
                this._logger.LogException("UseCustomAvatarFromLink", e);
            }
        }

        public string GetTrackString()
        {
            return this.trackString;
        }

        public bool IsTimerActive()
        {
            return this.timerEnabled;
        }
    }
}
